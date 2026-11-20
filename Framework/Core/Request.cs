using System.Net;
using System.Text;

namespace Lorcana.Engine;

public sealed class Request(HttpListenerRequest raw, CancellationToken cancellation)
{
    private const int BodyLimit = 64 * 1024;
    private Dictionary<string, string>? _form;
    public HttpListenerRequest Raw { get; } = raw;
    public CancellationToken Cancellation { get; } = cancellation;

    public async Task<Dictionary<string, string>> ReadFormAsync()
    {
        if (_form is not null)
            return _form;
        if (Raw.ContentLength64 > BodyLimit)
            throw new HttpProblem(413, "Форма слишком большая.");
        string? mediaType = Raw.ContentType?.Split(';')[0].Trim();
        if (!string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            throw new HttpProblem(415, "Ожидается application/x-www-form-urlencoded.");

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int count;
        while ((count = await Raw.InputStream.ReadAsync(chunk, Cancellation)) != 0)
        {
            if (buffer.Length + count > BodyLimit)
                throw new HttpProblem(413, "Форма слишком большая.");
            buffer.Write(chunk, 0, count);
        }

        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string body;
        try { body = new UTF8Encoding(false, true).GetString(buffer.ToArray()); }
        catch (DecoderFallbackException) { throw new HttpProblem(400, "Форма должна быть в UTF-8."); }
        foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = WebUtility.UrlDecode(parts[0]);
            string value = WebUtility.UrlDecode(parts.Length == 2 ? parts[1] : "");
            if (key.Length == 0 || form.Count >= 32 || !form.TryAdd(key, value))
                throw new HttpProblem(400, "Недопустимые или повторяющиеся поля формы.");
        }

        return _form = form;
    }
}
