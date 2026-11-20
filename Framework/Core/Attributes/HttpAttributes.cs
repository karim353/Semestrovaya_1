namespace Lorcana.Engine;

[AttributeUsage(AttributeTargets.Class)]
public sealed class HttpControllerAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public abstract class HttpRouteAttribute(string method, string? path) : Attribute
{
    public string Method { get; } = method;
    public string? Path { get; } = path;
}

public sealed class HttpGetAttribute(string? path = null) : HttpRouteAttribute("GET", path);
public sealed class HttpPostAttribute(string? path = null) : HttpRouteAttribute("POST", path);
