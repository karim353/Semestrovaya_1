using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Lorcana.App.Data;
using Xunit;

namespace Lorcana.Tests;

[Trait("Category", "Postgres")]
public sealed class PostgresTests : PostgresTestBase
{
    [Fact]
    public async Task AppRendersPagesAndSupportsAuthenticatedAdminWorkflow()
    {
        string url = EngineTests.FreeUrl();
        var templates = Directory.CreateTempSubdirectory("lorcana-views-");
        string app = Path.Combine(AppContext.BaseDirectory, "Lorcana.App.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { "exec", "--runtimeconfig", "Lorcana.Tests.runtimeconfig.json",
                     "--depsfile", "Lorcana.Tests.deps.json", app })
            start.ArgumentList.Add(argument);
        start.Environment["LORCANA_PREFIX"] = url;
        start.Environment["LORCANA_DB"] = _scoped!.ConnectionString;
        start.Environment["LORCANA_ADMIN_LOGIN"] = "testadmin";
        string password = Guid.NewGuid().ToString("N");
        start.Environment["LORCANA_ADMIN_PASSWORD"] = password;
        start.Environment["LORCANA_VIEWS_ROOT"] = templates.FullName;
        Process? process = null;
        try
        {
            foreach (string pageFolder in new[] { "ListCards", "CardEndpoint", "AdminEndpoint" })
                Directory.CreateDirectory(Path.Combine(templates.FullName, pageFolder));
            await File.WriteAllTextAsync(Path.Combine(templates.FullName, "ListCards/index.html"),
                "$foreach(card in Results.Cards)<p>${card.Name}</p>$endforeach");
            await File.WriteAllTextAsync(Path.Combine(templates.FullName, "CardEndpoint/index.html"), "<article>${Description}</article>");
            await File.WriteAllTextAsync(Path.Combine(templates.FullName, "AdminEndpoint/panel.html"), "${Login}");
            await File.WriteAllTextAsync(Path.Combine(templates.FullName, "AdminEndpoint/index.html"), "${Title}");
            process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var client = new HttpClient(new HttpClientHandler { UseCookies = true })
            {
                BaseAddress = new Uri(url),
                Timeout = TimeSpan.FromSeconds(5)
            };
            await WaitForHealth(client, process, stderr);
            // Docker/NAT: HTTP Host содержит внешний порт, а сокет слушает внутренний.
            using (var externalPortLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login"))
            {
                externalPortLogin.Headers.Host = "127.0.0.1:54321";
                externalPortLogin.Headers.Add("Origin", "http://127.0.0.1:54321");
                externalPortLogin.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["login"] = "testadmin", ["password"] = "wrong-password" });
                using var response = await client.SendAsync(externalPortLogin);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            foreach (string origin in new[] { "https://foreign.example", "null", "http://127.0.0.1:54322" })
            {
                using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
                crossOrigin.Headers.Add("Origin", origin);
                crossOrigin.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["login"] = "testadmin", ["password"] = password });
                using var response = await client.SendAsync(crossOrigin);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            client.DefaultRequestHeaders.Add("Origin", url.TrimEnd('/'));
            Assert.Contains("Amber Guide", await client.GetStringAsync("/"));
            using (var invalidFeedback = await Post(client, "/api/feedback",
                       new() { ["name"] = "Student", ["email"] = "broken", ["topic"] = "idea", ["message"] = "Test message" }))
                Assert.Equal(HttpStatusCode.BadRequest, invalidFeedback.StatusCode);
            using (var feedback = await Post(client, "/api/feedback",
                       new() { ["name"] = "Student", ["email"] = "student@example.test", ["topic"] = "idea", ["message"] = "Test message" }))
                Assert.Equal(HttpStatusCode.Created, feedback.StatusCode);
            Assert.Contains(await new Orm<Feedback>(_scoped!).ReadAllAsync(default), item => item.Message == "Test message");
            using (var unfiltered = JsonDocument.Parse(await client.GetStringAsync("/api/cards?ink=&cost=&inkable=&page=")))
                Assert.Equal(11, unfiltered.RootElement.GetProperty("total").GetInt64());
            using (var filtered = JsonDocument.Parse(await client.GetStringAsync("/api/cards?cost=9&inkable=false")))
                Assert.Equal(2, filtered.RootElement.GetProperty("total").GetInt64());
            using (var zeroCost = JsonDocument.Parse(await client.GetStringAsync("/api/cards?cost=0")))
            {
                var card = Assert.Single(zeroCost.RootElement.GetProperty("cards").EnumerateArray());
                Assert.Equal(0, card.GetProperty("cost").GetInt32());
            }
            using (var noApiSession = await client.GetAsync("/api/admin/session"))
                Assert.Equal(HttpStatusCode.Unauthorized, noApiSession.StatusCode);
            using (var noSession = await client.GetAsync("/admin"))
                Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
            using var login = await Post(client, "/api/auth/login", new() { ["login"] = "testadmin", ["password"] = password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Contains("HttpOnly", login.Headers.GetValues("Set-Cookie").Single());
            using var session = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            string csrf = session.RootElement.GetProperty("csrf").GetString()!;
            using (var activeSession = JsonDocument.Parse(await client.GetStringAsync("/api/admin/session")))
                Assert.Equal(csrf, activeSession.RootElement.GetProperty("csrf").GetString());
            Assert.Contains("testadmin", await client.GetStringAsync("/admin"));

            var fields = new Dictionary<string, string>
            {
                ["name"] = "Admin card", ["code"] = "TEST-2", ["inkId"] = "1", ["setId"] = "1",
                ["rarityId"] = "1", ["cost"] = "6", ["inkable"] = "true",
                ["descriptionHtml"] = "</div><strong>Bold</strong><script>alert(1)</script>"
            };
            using (var forbidden = await Post(client, "/api/admin/cards/create", fields))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            fields["csrf"] = csrf;
            using var created = await Post(client, "/api/admin/cards/create", fields);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            int id = createdJson.RootElement.GetProperty("id").GetInt32();
            string detail = await client.GetStringAsync($"/cards/detail/{id}");
            Assert.Contains("<strong>Bold</strong>", detail);
            Assert.Contains("&lt;script&gt;", detail);
            Assert.DoesNotContain("</div>", detail);
            using (var duplicate = await Post(client, "/api/admin/cards/create", fields))
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            fields["id"] = id.ToString();
            fields["name"] = "Updated card";
            using (var updated = await Post(client, "/api/admin/cards/update", fields))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Contains("Updated card", await client.GetStringAsync($"/api/cards/detail/{id}"));
            using (var deleted = await Post(client, "/api/admin/cards/delete", fields))
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            using (var missing = await client.GetAsync($"/api/cards/detail/{id}"))
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using (var logout = await Post(client, "/api/auth/logout", new() { ["csrf"] = csrf }))
                Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
            using (var noSession = await client.GetAsync("/admin"))
                Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);

            // Дополнительно рендерим настоящие шаблоны, а не только минимальные заглушки.
            foreach (string view in new[] { "ListCards/index", "CardEndpoint/index" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "actual-views", view + ".html"),
                    Path.Combine(templates.FullName, view + ".html"), overwrite: true);
            string gallery = await client.GetStringAsync("/cards?ink=Amber&cost=9&inkable=false&sort=cost");
            Assert.Contains("value=\"Amber\" checked", gallery);
            Assert.Contains("value=\"false\" checked", gallery);
            Assert.Contains("value=\"9\" checked", gallery);
            Assert.Contains("value=\"cost\" selected", gallery);
            Assert.DoesNotContain("<script", gallery, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$foreach", gallery);
            string zeroCostGallery = await client.GetStringAsync("/cards?cost=0");
            Assert.Contains("name=\"cost\" value=\"0\" checked", zeroCostGallery);
            Assert.Contains("Steel Token", zeroCostGallery);
            Assert.DoesNotContain("Steel Colossus", zeroCostGallery);
            int fixtureId = (await new Orm<Card>(_scoped!).ReadAllAsync(default))
                .Single(card => card.Name == "Amber Guide").Id;
            string publicDetail = await client.GetStringAsync($"/cards/detail/{fixtureId}");
            Assert.Contains("Amber Guide", publicDetail);
            Assert.DoesNotContain("<script", publicDetail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("${", publicDetail);

            await process.StandardInput.WriteLineAsync("stop");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Contains("Сервер остановлен", await stdout);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            process?.Dispose();
            templates.Delete(recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(path, content);
    }

    private static async Task WaitForHealth(HttpClient client, Process process, Task<string> errors)
    {
        for (int i = 0; i < 40; i++)
        {
            if (process.HasExited)
                throw new InvalidOperationException(await errors);
            try
            {
                using var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(125);
        }
        throw new TimeoutException("Сервер не стал доступен.");
    }
}
