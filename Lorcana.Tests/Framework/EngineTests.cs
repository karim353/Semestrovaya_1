using System.Net;
using System.Net.Sockets;
using Lorcana.Engine;
using Xunit;

namespace Lorcana.Tests;

public sealed class EngineTests
{
    [Fact]
    public async Task RoutesBothVerbsAndSurvivesFailures()
    {
        string url = FreeUrl();
        var chain = new HandlerChain(new ControllerHandler("api", new ProbeController()));
        await using var server = new HttpServer([url], chain, _ => { });
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
        Assert.Contains("42", await client.GetStringAsync("probe/echo/42"));
        Assert.Contains("43", await client.GetStringAsync("api/probe/echo?id=43"));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = "A & B" });
        using var post = await client.PostAsync("probe/echo", form);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("A \\u0026 B", await post.Content.ReadAsStringAsync());
        await AssertStatus(client, "probe/echo/abc", HttpStatusCode.BadRequest);
        await AssertStatus(client, "probe/echo?id=1&id=2", HttpStatusCode.BadRequest);
        using (var wrongVerb = await client.PutAsync("probe/echo", null))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongVerb.StatusCode);
            Assert.Contains("GET", wrongVerb.Content.Headers.Allow);
        }
        await AssertStatus(client, "probe/unknown", HttpStatusCode.NotFound);
        await AssertStatus(client, "probe/unauthorized", HttpStatusCode.Unauthorized);
        await AssertStatus(client, "probe/fail", HttpStatusCode.InternalServerError);
        await AssertStatus(client, "probe/echo/9", HttpStatusCode.OK);
        using var tooBig = new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = new string('a', 70_000) });
        using var rejected = await client.PostAsync("probe/echo", tooBig);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ServesFilesAndCustom404()
    {
        string url = FreeUrl();
        var folder = Directory.CreateTempSubdirectory("lorcana-tests-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "style.css"), "body {}");
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "font.ttf"), [0, 1, 0, 0]);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "404.html"), "Not here");
            var chain = new HandlerChain(new StaticFilesHandler(folder.FullName), new NotFoundHandler(folder.FullName));
            await using var server = new HttpServer([url], chain, _ => { });
            server.Start();
            using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
            using var css = await client.GetAsync("style.css");
            Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
            Assert.Equal("body {}", await css.Content.ReadAsStringAsync());
            using var font = await client.GetAsync("font.ttf");
            Assert.Equal(HttpStatusCode.OK, font.StatusCode);
            Assert.Equal("font/ttf", font.Content.Headers.ContentType?.MediaType);
            using var missing = await client.GetAsync("missing");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("Not here", await missing.Content.ReadAsStringAsync());
            await AssertStatus(client, ".env", HttpStatusCode.BadRequest);
            await AssertStatus(client, "api/missing", HttpStatusCode.NotFound);
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task AssertStatus(HttpClient client, string path, HttpStatusCode expected)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    internal static string FreeUrl()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return $"http://127.0.0.1:{port}/";
    }

    [HttpController("probe")]
    public sealed class ProbeController
    {
        [HttpGet]
        public Task<IHttpResult> Echo(int id) => Task.FromResult<IHttpResult>(HttpResult.Json(new { id }));

        [HttpPost]
        public Task<IHttpResult> Echo(string message) => Task.FromResult<IHttpResult>(HttpResult.Json(new { message }));

        [HttpGet]
        public Task<IHttpResult> Unauthorized() => throw new HttpProblem(401, "Требуется вход.");

        [HttpGet]
        public async Task<IHttpResult> Fail()
        {
            await Task.Yield();
            throw new InvalidOperationException("Test exception");
        }
    }
}
