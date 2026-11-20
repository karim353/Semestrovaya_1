using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;

namespace Lorcana.App.Data;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute(string name, bool key = false) : Attribute
{
    public string Name { get; } = name;
    public bool Key { get; } = key;
}

public sealed class Orm<T> where T : class, new()
{
    private sealed record Column(string Name, bool Key, PropertyInfo Property);
    private static readonly string Table = Quote(typeof(T).GetCustomAttribute<TableAttribute>()?.Name
        ?? throw new InvalidOperationException("Требуется атрибут Table."));
    private static readonly Column[] Columns = typeof(T).GetProperties()
        .Select(p => (Property: p, Attribute: p.GetCustomAttribute<ColumnAttribute>()))
        .Where(pair => pair.Attribute is not null)
        .Select(pair => new Column(Quote(pair.Attribute!.Name), pair.Attribute.Key, pair.Property))
        .ToArray();
    private static readonly Column Key = Columns.Single(c => c.Key);
    private static readonly Column[] Fields = Columns.Where(c => !c.Key).ToArray();
    private static readonly string Projection = string.Join(", ", Columns.Select(c => c.Name));
    private readonly NpgsqlDataSource _data;

    public Orm(NpgsqlDataSource data) => _data = data;

    public async Task<int> CreateAsync(T entity, CancellationToken cancellation)
    {
        string names = string.Join(", ", Fields.Select(c => c.Name));
        string values = string.Join(", ", Fields.Select((_, i) => $"@p{i}"));
        await using var command = _data.CreateCommand($"INSERT INTO {Table} ({names}) VALUES ({values}) RETURNING {Key.Name}");
        AddFields(command, entity);
        return (int)(await command.ExecuteScalarAsync(cancellation))!;
    }

    public async Task<T?> ReadByIdAsync(int id, CancellationToken cancellation)
    {
        await using var command = _data.CreateCommand($"SELECT {Projection} FROM {Table} WHERE {Key.Name} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<T>> ReadAllAsync(CancellationToken cancellation)
    {
        await using var command = _data.CreateCommand($"SELECT {Projection} FROM {Table} ORDER BY {Key.Name}");
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellation))
            rows.Add(Map(reader));
        return rows;
    }

    public async Task<bool> UpdateAsync(int id, T entity, CancellationToken cancellation)
    {
        string assignments = string.Join(", ", Fields.Select((c, i) => $"{c.Name} = @p{i}"));
        await using var command = _data.CreateCommand($"UPDATE {Table} SET {assignments} WHERE {Key.Name} = @id");
        AddFields(command, entity);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellation) == 1;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellation)
    {
        await using var command = _data.CreateCommand($"DELETE FROM {Table} WHERE {Key.Name} = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellation) == 1;
    }

    private static void AddFields(NpgsqlCommand command, T entity)
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            object? value = Fields[i].Property.GetValue(entity);
            if (value is not null)
                command.Parameters.AddWithValue($"p{i}", value);
            else
            {
                Type type = Nullable.GetUnderlyingType(Fields[i].Property.PropertyType) ?? Fields[i].Property.PropertyType;
                command.Parameters.Add(new NpgsqlParameter($"p{i}", DBNull.Value)
                {
                    DbType = type == typeof(int) ? System.Data.DbType.Int32 :
                        type == typeof(bool) ? System.Data.DbType.Boolean :
                        type == typeof(string) ? System.Data.DbType.String :
                        throw new NotSupportedException($"Неподдерживаемый тип: {type.Name}")
                });
            }
        }
    }

    private static T Map(DbDataReader reader)
    {
        var entity = new T();
        for (int i = 0; i < Columns.Length; i++)
            Columns[i].Property.SetValue(entity, reader.IsDBNull(i) ? null : reader.GetValue(i));
        return entity;
    }

    private static string Quote(string name)
    {
        if (!Regex.IsMatch(name, @"\A[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Недопустимое имя таблицы или столбца.");
        return "\"" + name + "\"";
    }
}
