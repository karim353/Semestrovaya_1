namespace Lorcana.Engine;

public sealed class StaticFilesHandler(string root) : IRequestHandler
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".json"] = "application/json",
        [".svg"] = "image/svg+xml", [".png"] = "image/png", [".webp"] = "image/webp",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".avif"] = "image/avif",
        [".ico"] = "image/x-icon", [".woff2"] = "font/woff2", [".woff"] = "font/woff", [".ttf"] = "font/ttf"
    };

    public async Task<IHttpResult?> HandleAsync(Request request)
    {
        if (request.Raw.HttpMethod != "GET")
            return null;
        string path = request.Raw.RawUrl?.Split('?', 2)[0] ?? "";
        string[] parts = Uri.UnescapeDataString(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null; // Главная страница принадлежит MVC.
        foreach (string part in parts)
            if (part.StartsWith('.') || part.Contains('\\') || part.Contains(':')
                || part.Any(char.IsControl) || part.TrimEnd(' ', '.') != part)
                throw new HttpProblem(400, "Недопустимый путь.");

        string file = Path.GetFullPath(root);
        foreach (string part in parts)
        {
            file = Path.Combine(file, part);
            if (IsLink(file))
                return null;
        }
        if (Directory.Exists(file))
        {
            file = Path.Combine(file, "index.html");
            if (IsLink(file) || !File.Exists(file))
                return null;
            if (!path.EndsWith('/'))
            {
                // Относительная ссылка, сформированная из проверенных сегментов.
                return HttpResult.Html("", 308) with
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["Location"] = "/" + string.Join('/', parts.Select(Uri.EscapeDataString)) + "/"
                    }
                };
            }
        }

        if (!File.Exists(file))
            return null;
        if (new FileInfo(file).Length > 16 * 1024 * 1024)
            throw new HttpProblem(413, "Статический файл слишком большой.");
        return new HttpResult(200, MimeTypes.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream"),
            await File.ReadAllBytesAsync(file, request.Cancellation));
    }

    private static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}

public sealed class NotFoundHandler(string staticRoot) : IRequestHandler
{
    public async Task<IHttpResult?> HandleAsync(Request request)
    {
        if (request.Raw.Url?.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) != true)
        {
            string page = Path.Combine(staticRoot, "404.html");
            if (File.Exists(page))
                return HttpResult.Html(await File.ReadAllTextAsync(page, request.Cancellation), 404);
        }
        return HttpResult.Error(404, "Страница не найдена.");
    }
}
