using System.Net;
using System.Text;
using System.Text.Json;

namespace Lorcana.Engine;

public interface IHttpResult
{
    int Status { get; }
    Task WriteAsync(HttpListenerResponse response, CancellationToken cancellation);
}

public sealed record HttpResult(int Status, string ContentType, byte[] Content,
    IReadOnlyDictionary<string, string>? Headers = null) : IHttpResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpResult Json(object value, int status = 200) =>
        new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    public static HttpResult Html(string html, int status = 200) =>
        new(status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public static HttpResult Error(int status, string message) => Json(new { error = message }, status);

    public async Task WriteAsync(HttpListenerResponse response, CancellationToken cancellation)
    {
        response.StatusCode = Status;
        response.ContentType = ContentType;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        if (Headers is not null)
            foreach (var header in Headers)
                response.Headers[header.Key] = header.Value;
        response.ContentLength64 = Content.Length;
        if (Content.Length > 0)
            await response.OutputStream.WriteAsync(Content, cancellation);
    }
}

public sealed class HttpProblem(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
