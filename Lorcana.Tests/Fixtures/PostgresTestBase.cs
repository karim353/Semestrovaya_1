using Npgsql;
using Xunit;

namespace Lorcana.Tests;

public abstract class PostgresTestBase : IAsyncLifetime
{
    private readonly string _schema = "test_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource? _database;
    protected NpgsqlDataSource? _scoped;

    public async Task InitializeAsync()
    {
        string connection = Environment.GetEnvironmentVariable("LORCANA_TEST_DB") ??
            throw new InvalidOperationException("Укажите LORCANA_TEST_DB (тестовая БД, с правом CREATE SCHEMA).");
        _database = NpgsqlDataSource.Create(connection);
        await using (var create = _database.CreateCommand($"CREATE SCHEMA {_schema}"))
            await create.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(connection) { SearchPath = _schema };
        _scoped = NpgsqlDataSource.Create(settings.ConnectionString);
        foreach (string script in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "sql"), "*.sql").Order())
        {
            await using var command = _scoped.CreateCommand(await File.ReadAllTextAsync(script));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_scoped is not null)
            await _scoped.DisposeAsync();
        if (_database is not null)
        {
            await using var drop = _database.CreateCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE");
            await drop.ExecuteNonQueryAsync();
            await _database.DisposeAsync();
        }
    }
}
