using System.Net;
using System.Text.Json;

namespace Lorcana.Engine;

public sealed class HttpServer(IEnumerable<string> prefixes, HandlerChain chain, Action<string> log) : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private Task[] _workers = [];

    public void Start()
    {
        foreach (string prefix in prefixes)
            _listener.Prefixes.Add(prefix);
        _listener.Start();
        _workers = Enumerable.Range(0, 16).Select(_ => ListenAsync()).ToArray();
        log($"Сервер запущен: {string.Join(", ", _listener.Prefixes)}");
    }

    public async Task StopAsync()
    {
        if (!_listener.IsListening)
            return;
        _listener.Stop();
        await Task.WhenAll(_workers);
        log("Сервер остановлен.");
    }

    private async Task ListenAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                if (!_listener.IsListening)
                    break;
                log($"Ошибка приёма запроса: {ex.GetType().Name}");
                await Task.Delay(100);
                continue;
            }
            await HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            IHttpResult result;
            try { result = await chain.HandleAsync(new Request(context.Request, timeout.Token)); }
            catch (HttpProblem ex) { result = HttpResult.Error(ex.Status, ex.Message); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                result = HttpResult.Error(408, "Время обработки запроса истекло.");
            }
            catch (Exception ex)
            {
                log($"Ошибка запроса: {ex.GetType().Name}");
                result = HttpResult.Error(500, "Внутренняя ошибка сервера.");
            }

            if (result is HttpResult error && error.Status >= 400 && error.ContentType.StartsWith("application/json") &&
                context.Request.HttpMethod == "GET" &&
                context.Request.Url?.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) != true &&
                context.Request.Url?.AbsolutePath != "/health" &&
                context.Request.AcceptTypes?.Any(type => type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) == true)
            {
                using var payload = JsonDocument.Parse(error.Content);
                string message = payload.RootElement.TryGetProperty("error", out var value) &&
                    value.ValueKind == JsonValueKind.String ? value.GetString()! : "Не удалось открыть страницу.";
                result = HttpResult.Html(
                    $"""<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{error.Status} · Lorcana</title><link rel="stylesheet" href="/assets/style.css"><main class="wrap"><h1>{error.Status}</h1><p>{WebUtility.HtmlEncode(message)}</p><p><a href="/">Каталог</a> · <a href="/auth/login">Вход</a></p></main></html>""",
                    error.Status) with { Headers = error.Headers };
            }
            await result.WriteAsync(context.Response,
                timeout.IsCancellationRequested ? CancellationToken.None : timeout.Token);
            log($"{context.Request.HttpMethod} → {result.Status}");
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException
            or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            log($"Ответ прерван: {ex.GetType().Name}");
        }
        finally
        {
            try { context.Response.Close(); }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener.Close();
    }
}
