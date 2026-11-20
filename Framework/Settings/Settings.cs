using System.Text.Json;

namespace Lorcana.Engine;

public sealed record Settings(string[] Prefixes, string StaticRoot, string ViewsRoot, string ApiPrefix)
{
    private static readonly Lazy<Settings> Current = new(Load);
    public static Settings Instance => Current.Value;

    private static Settings Load()
    {
        string basePath = AppContext.BaseDirectory;
        var config = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(basePath, "settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("settings.json пуст.");
        string? overridePrefix = Environment.GetEnvironmentVariable("LORCANA_PREFIX");
        string[] prefixes = string.IsNullOrWhiteSpace(overridePrefix) ? config.Prefixes : [overridePrefix];
        if (prefixes is not { Length: > 0 } || prefixes.Any(p => string.IsNullOrWhiteSpace(p) || !p.EndsWith('/')))
            throw new ArgumentException("Задайте хотя бы один HTTP-префикс с / на конце.");
        return config with
        {
            Prefixes = prefixes,
            StaticRoot = Path.GetFullPath(config.StaticRoot, basePath),
            ViewsRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("LORCANA_VIEWS_ROOT") ?? config.ViewsRoot, basePath)
        };
    }
}
