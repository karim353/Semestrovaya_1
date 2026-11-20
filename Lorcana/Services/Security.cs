using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lorcana.Engine;
using Npgsql;

namespace Lorcana.App;

internal sealed class Authentication(NpgsqlDataSource data)
{
    private const int Iterations = 600_000;
    private const string Cookie = "lorcana_session";
    private static readonly Regex LoginPattern = new(@"\A[a-zA-Z0-9_.-]{3,32}\z");
    private static readonly Regex RichTags = new(@"</?(?:p|div|strong|b|em|i|u|ul|ol|li|br)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly object _sessionGate = new();
    private readonly object _attemptGate = new();
    private readonly Dictionary<string, (DateTimeOffset Until, int Count)> _attempts = new();
    internal sealed record Session(string Login, string Role, string Csrf, DateTimeOffset Expires);

    public async Task EnsureAdminAsync(string login, string password, CancellationToken cancellation)
    {
        if (!LoginPattern.IsMatch(login) || password.Length is < 12 or > 128)
            throw new ArgumentException("Логин администратора: 3–32 символа. Пароль: 12–128 символов.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        await using var command = data.CreateCommand("""
            INSERT INTO users (login, password_salt, password_hash, role)
            VALUES (@login, @salt, @hash, 'admin') ON CONFLICT (login) DO NOTHING
            """);
        command.Parameters.AddWithValue("login", login.ToLowerInvariant());
        command.Parameters.AddWithValue("salt", salt);
        command.Parameters.AddWithValue("hash", Hash(password, salt));
        await command.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<IHttpResult> LoginAsync(Request request, string login, string password)
    {
        RequireSameOrigin(request);
        if (!LoginPattern.IsMatch(login) || password.Length is < 1 or > 128)
            return HttpResult.Error(400, "Некорректный формат логина или пароля.");
        RateLimit(request);
        string normalized = login.ToLowerInvariant();
        byte[] salt = new byte[16];
        byte[] hash = new byte[32];
        string? role = null;
        await using (var command = data.CreateCommand(
            "SELECT password_salt, password_hash, role FROM users WHERE login = @login"))
        {
            command.Parameters.AddWithValue("login", normalized);
            await using var reader = await command.ExecuteReaderAsync(request.Cancellation);
            if (await reader.ReadAsync(request.Cancellation))
            {
                salt = reader.GetFieldValue<byte[]>(0);
                hash = reader.GetFieldValue<byte[]>(1);
                role = reader.GetString(2);
            }
        }

        if (!CryptographicOperations.FixedTimeEquals(Hash(password, salt), hash) || role is null)
            return HttpResult.Error(401, "Неверный логин или пароль.");

        string token = Token();
        var session = new Session(normalized, role, Token(), DateTimeOffset.UtcNow.AddHours(4));
        lock (_sessionGate)
        {
            string? old = request.Raw.Cookies[Cookie]?.Value;
            if (old is not null)
                _sessions.TryRemove(old, out _);
            foreach (var entry in _sessions)
                if (entry.Value.Expires <= DateTimeOffset.UtcNow)
                    _sessions.TryRemove(entry.Key, out _);
            if (_sessions.Count >= 1000)
                return HttpResult.Error(503, "Достигнут лимит активных сессий.");
            _sessions[token] = session;
        }
        return SessionResult(request, session, token);
    }

    public Session RequireAdmin(Request request)
    {
        string? token = request.Raw.Cookies[Cookie]?.Value;
        if (token is null || !_sessions.TryGetValue(token, out var session) || session.Expires <= DateTimeOffset.UtcNow)
            throw new HttpProblem(401, "Требуется вход.");
        if (session.Role != "admin")
            throw new HttpProblem(403, "Требуется роль администратора.");
        return session;
    }

    public async Task AuthorizeWriteAsync(Request request)
    {
        RequireSameOrigin(request);
        var session = RequireAdmin(request);
        var form = await request.ReadFormAsync();
        string? token = request.Raw.Headers["X-CSRF-Token"] ?? form.GetValueOrDefault("csrf");
        if (token is null || token.Length != session.Csrf.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(session.Csrf)))
            throw new HttpProblem(403, "Некорректный CSRF-токен.");
    }

    public async Task<IHttpResult> LogoutAsync(Request request)
    {
        await AuthorizeWriteAsync(request);
        _sessions.TryRemove(request.Raw.Cookies[Cookie]!.Value!, out _);
        return HttpResult.Json(new { signedOut = true }) with { Headers = CookieHeader(request, "", 0) };
    }

    public static string CleanDescription(string input)
    {
        // Сохраняем только теги без атрибутов. Весь остальной ввод остаётся обычным текстом.
        var output = new StringBuilder();
        var openTags = new Stack<string>();
        int offset = 0;
        foreach (Match tag in RichTags.Matches(input))
        {
            output.Append(EncodeEditorText(input[offset..tag.Index]));
            string markup = tag.Value.ToLowerInvariant();
            if (markup == "<br>")
                output.Append(markup);
            else if (!markup.StartsWith("</", StringComparison.Ordinal))
            {
                openTags.Push(markup[1..^1]);
                output.Append(markup);
            }
            else if (openTags.TryPeek(out string? name) && markup[2..^1] == name)
            {
                openTags.Pop();
                output.Append(markup);
            }
            offset = tag.Index + tag.Length;
        }
        output.Append(EncodeEditorText(input[offset..]));
        foreach (string tag in openTags)
            output.Append("</").Append(tag).Append('>');
        return output.ToString();
    }

    private static string EncodeEditorText(string value) => WebUtility.HtmlEncode(WebUtility.HtmlDecode(value));

    private void RateLimit(Request request)
    {
        string address = request.Raw.RemoteEndPoint?.Address.ToString() ?? "unknown";
        lock (_attemptGate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (var key in _attempts.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray())
                _attempts.Remove(key);
            var attempt = _attempts.GetValueOrDefault(address, (Until: now.AddMinutes(1), Count: 0));
            if (attempt.Count >= 8 || (!_attempts.ContainsKey(address) && _attempts.Count >= 1000))
                throw new HttpProblem(429, "Слишком много попыток входа. Повторите через минуту.");
            _attempts[address] = (attempt.Until, attempt.Count + 1);
        }
    }

    internal static void RequireSameOrigin(Request request)
    {
        string? origin = request.Raw.Headers["Origin"];
        // В Docker внешний порт из Host может отличаться от локального порта HttpListener.
        // Не доверяем X-Forwarded-*; браузерный Origin сравниваем с фактическим Host запроса.
        string scheme = request.Raw.IsSecureConnection ? "https" : "http";
        string? host = request.Raw.Headers["Host"];
        string? expected = Uri.TryCreate($"{scheme}://{host}", UriKind.Absolute, out var address) &&
            address.UserInfo.Length == 0 && address.AbsolutePath == "/" &&
            address.Query.Length == 0 && address.Fragment.Length == 0
                ? address.GetLeftPart(UriPartial.Authority) : null;
        if (string.Equals(request.Raw.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase) ||
            expected is null ||
            (origin is not null && !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase)))
            throw new HttpProblem(403, "Запрос должен исходить с того же сайта.");
    }

    private static HttpResult SessionResult(Request request, Session session, string token) =>
        HttpResult.Json(new { session.Login, session.Role, session.Csrf }) with
        {
            Headers = CookieHeader(request, token, 14400)
        };

    private static Dictionary<string, string> CookieHeader(Request request, string token, int maxAge) => new()
    {
        ["Set-Cookie"] = $"{Cookie}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age={maxAge}" +
            (request.Raw.IsSecureConnection ? "; Secure" : "")
    };

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
