using System.Runtime.InteropServices;
using System.Text.Json;
using Lorcana.App;
using Lorcana.App.Data;
using Lorcana.Engine;
using Npgsql;

try
{
    var settings = Settings.Instance;
    var connection = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LORCANA_DB") ?? "Host=127.0.0.1;Database=lorcana;Username=lorcana")
    {
        Timeout = 5,
        CommandTimeout = 8
    };
    await using var data = NpgsqlDataSource.Create(connection.ConnectionString);
    var auth = new Authentication(data);
    string? login = Environment.GetEnvironmentVariable("LORCANA_ADMIN_LOGIN");
    string? password = Environment.GetEnvironmentVariable("LORCANA_ADMIN_PASSWORD");
    if (login is not null || password is not null)
    {
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Для создания администратора укажите обе переменные LORCANA_ADMIN_*.");
        using var bootstrap = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await auth.EnsureAdminAsync(login, password, bootstrap.Token);
    }

    var catalog = new CatalogRepository(data);
    var views = new Views(settings.ViewsRoot);
    var routing = new ControllerHandler(settings.ApiPrefix, new CardsController(catalog, views),
        new AuthController(auth, views), new AdminController(auth, catalog, views), new HealthController(data),
        new FeedbackController(new Orm<Feedback>(data), views));
    var chain = new HandlerChain(new StaticFilesHandler(settings.StaticRoot),
        new DatabaseErrors(routing), new NotFoundHandler(settings.StaticRoot));

    await using var server = new HttpServer(settings.Prefixes, chain, message =>
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}"));
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        shutdown.Cancel();
    };
    using var sigterm = OperatingSystem.IsWindows() ? null :
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });
    server.Start();
    Console.WriteLine("Введите stop для остановки.");
    // ReadLine не должен задерживать завершение процесса при SIGTERM или Ctrl+C.
    _ = Task.Run(() =>
    {
        while (Console.ReadLine() is { } line)
        {
            if (!line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                continue;
            shutdown.Cancel();
            break;
        }
    });
    try { await Task.Delay(Timeout.Infinite, shutdown.Token); }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    await server.StopAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex is ArgumentException or JsonException or FileNotFoundException
        ? $"Настройки запуска: {ex.Message}"
        : $"Сервер не запустился ({ex.GetType().Name}). Проверьте порт, настройки и подключение к БД.");
    return 1;
}

internal sealed class DatabaseErrors(IRequestHandler next) : IRequestHandler
{
    public async Task<IHttpResult?> HandleAsync(Request request)
    {
        try { return await next.HandleAsync(request); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return HttpResult.Error(409, "Запись с таким кодом уже существует.");
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            return HttpResult.Error(400, "Проверьте ограничения и выбранные справочники.");
        }
        catch (NpgsqlException ex)
        {
            Console.Error.WriteLine(ex is PostgresException pg
                ? $"Ошибка PostgreSQL: {pg.SqlState}" : $"Ошибка соединения с БД: {ex.GetType().Name}");
            return HttpResult.Error(503, "База данных недоступна. Повторите запрос позже.");
        }
    }
}
