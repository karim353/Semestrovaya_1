using System.Globalization;
using System.Reflection;

namespace Lorcana.Engine;

public sealed class ControllerHandler : IRequestHandler
{
    private sealed record Endpoint(object Controller, MethodInfo Method, ParameterInfo[] Parameters);
    private readonly Dictionary<string, Dictionary<string, Endpoint>> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public ControllerHandler(string apiPrefix, params object[] controllers)
    {
        foreach (object controller in controllers)
        {
            Type type = controller.GetType();
            var label = type.GetCustomAttribute<HttpControllerAttribute>() ??
                throw new ArgumentException($"У {type.Name} отсутствует HttpController.");
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            foreach (var route in method.GetCustomAttributes<HttpRouteAttribute>())
            {
                if (method.ReturnType != typeof(Task<IHttpResult>))
                    throw new ArgumentException($"Действие {method.Name} должно возвращать Task<IHttpResult>.");
                var endpoint = new Endpoint(controller, method, method.GetParameters());
                string conventional = $"{label.Name}/{method.Name}";
                if (route.Path is null)
                {
                    Register(conventional, route.Method, endpoint);
                    if (!string.IsNullOrWhiteSpace(apiPrefix))
                        Register($"{apiPrefix}/{conventional}", route.Method, endpoint);
                }
                else
                {
                    Register(route.Path, route.Method, endpoint);
                }
            }
        }
    }

    private void Register(string path, string verb, Endpoint endpoint)
    {
        path = path.Trim('/');
        if (!_routes.TryGetValue(path, out var methods))
            _routes[path] = methods = new(StringComparer.Ordinal);
        if (!methods.TryAdd(verb, endpoint))
            throw new ArgumentException($"Маршрут {verb} /{path} объявлен дважды.");
    }

    public async Task<IHttpResult?> HandleAsync(Request request)
    {
        string path = request.Raw.Url?.AbsolutePath.Trim('/') ?? "";
        string[] segments = path.Length == 0 ? [] : path.Split('/');
        Dictionary<string, Endpoint>? methods = null;
        int length = segments.Length;
        // Корневой маршрут обрабатывает только пустой путь.
        while (length >= (segments.Length == 0 ? 0 : 1))
        {
            if (_routes.TryGetValue(string.Join('/', segments.Take(length)), out methods))
                break;
            length--;
        }
        if (methods is null)
            return null;
        if (!methods.TryGetValue(request.Raw.HttpMethod, out var endpoint))
            return HttpResult.Error(405, "HTTP-метод не поддерживается.") with
            {
                Headers = new Dictionary<string, string> { ["Allow"] = string.Join(", ", methods.Keys.Order()) }
            };

        var form = request.Raw.HttpMethod == "POST" ? await request.ReadFormAsync() : null;
        var arguments = new object?[endpoint.Parameters.Length];
        for (int i = 0; i < endpoint.Parameters.Length; i++)
        {
            var parameter = endpoint.Parameters[i];
            if (parameter.ParameterType == typeof(Request))
            {
                arguments[i] = request;
                continue;
            }
            string name = parameter.Name!;
            string? value;
            if (form is not null)
                value = form.GetValueOrDefault(name);
            else if (length < segments.Length)
                value = Uri.UnescapeDataString(segments[length++]);
            else
            {
                var values = request.Raw.QueryString.GetValues(name);
                if (values?.Length > 1)
                    throw new HttpProblem(400, $"Параметр {name} указан несколько раз.");
                value = values?.FirstOrDefault();
            }
            arguments[i] = Bind(parameter, value);
        }
        if (length < segments.Length)
            throw new HttpProblem(400, "Лишние сегменты маршрута.");
        return await (Task<IHttpResult>)endpoint.Method.Invoke(endpoint.Controller,
            BindingFlags.DoNotWrapExceptions, binder: null, parameters: arguments, culture: null)!;
    }

    private static object? Bind(ParameterInfo parameter, string? value)
    {
        if (parameter.HasDefaultValue && (value is null ||
            (value.Length == 0 && (parameter.DefaultValue is null || parameter.ParameterType != typeof(string)))))
            return parameter.DefaultValue;
        Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        if (value is not null)
        {
            if (type == typeof(string))
                return value;
            if (type == typeof(int) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return number;
            if (type == typeof(bool) && bool.TryParse(value, out bool flag))
                return flag;
        }
        throw new HttpProblem(400, $"Некорректное значение параметра {parameter.Name}.");
    }
}
