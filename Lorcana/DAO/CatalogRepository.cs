using Npgsql;

namespace Lorcana.App.Data;

public sealed class CatalogRepository(NpgsqlDataSource data)
{
    public Orm<Card> Cards { get; } = new(data);
    private const string From = """
        FROM cards c JOIN inks i ON c.ink_id = i.id
        LEFT JOIN inks i2 ON c.second_ink_id = i2.id
        JOIN card_sets s ON c.set_id = s.id JOIN rarities r ON c.rarity_id = r.id
        """;
    private const string Projection = """
        c.id, c.name, c.code, i.name, s.name, r.name, c.cost, c.inkable, c.image_url,
        c.identifier, c.card_type, i2.name
        """;

    public async Task<CatalogPage> SearchAsync(Filters filters, CancellationToken cancellation)
    {
        const string where = """
            WHERE (@q = '' OR strpos(lower(c.name), lower(@q)) > 0 OR strpos(lower(c.identifier), lower(@q)) > 0)
            AND (@ink IS NULL OR lower(i.name) = lower(@ink) OR lower(i2.name) = lower(@ink))
            AND (@cost IS NULL OR c.cost = @cost OR (@cost = 9 AND c.cost > 9))
            AND (@inkable IS NULL OR c.inkable = @inkable)
            AND (@rarity IS NULL OR lower(r.name) = lower(@rarity))
            AND (@type IS NULL OR c.card_type = @type)
            """;
        await using var connection = await data.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellation);
        await using var countCommand = new NpgsqlCommand($"SELECT count(*) {From} {where}", connection, transaction);
        AddFilters(countCommand, filters);
        long total = (long)(await countCommand.ExecuteScalarAsync(cancellation))!;
        string order = filters.Sort switch
        {
            "name" => "lower(c.name), c.id",
            "cost" => "c.cost, lower(c.name), c.id",
            _ => "c.id DESC"
        };
        await using var query = new NpgsqlCommand(
            $"SELECT {Projection} {From} {where} ORDER BY {order} LIMIT @limit OFFSET @offset", connection, transaction);
        AddFilters(query, filters);
        query.Parameters.AddWithValue("limit", Filters.PageSize);
        query.Parameters.AddWithValue("offset", (filters.Page - 1) * Filters.PageSize);
        var cards = new List<CardSummary>();
        await using (var reader = await query.ExecuteReaderAsync(cancellation))
            while (await reader.ReadAsync(cancellation))
                cards.Add(ReadSummary(reader));
        await transaction.CommitAsync(cancellation);
        return new(cards, filters.Page, Filters.PageSize, total, filters.Page > 1,
            (long)filters.Page * Filters.PageSize < total);
    }

    public async Task<CardDetail?> DetailAsync(int id, CancellationToken cancellation)
    {
        await using var query = data.CreateCommand($"SELECT {Projection}, c.description_html, c.flavor, c.illustrator {From} WHERE c.id = @id");
        query.Parameters.AddWithValue("id", id);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation)
            ? new(ReadSummary(reader), reader.GetString(12), reader.GetString(13), reader.GetString(14)) : null;
    }

    public async Task<CatalogOptions> OptionsAsync(CancellationToken cancellation) =>
        new(await new Orm<Ink>(data).ReadAllAsync(cancellation),
            await new Orm<CardSet>(data).ReadAllAsync(cancellation),
            await new Orm<Rarity>(data).ReadAllAsync(cancellation));

    private static CardSummary ReadSummary(NpgsqlDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.GetString(5), r.GetInt32(6), r.GetBoolean(7), r.GetString(8), r.GetString(9),
            r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11));

    private static void AddFilters(NpgsqlCommand command, Filters filters)
    {
        command.Parameters.AddWithValue("q", filters.Q);
        command.Parameters.AddWithValue("ink", NpgsqlTypes.NpgsqlDbType.Text, (object?)filters.Ink ?? DBNull.Value);
        command.Parameters.AddWithValue("cost", NpgsqlTypes.NpgsqlDbType.Integer, (object?)filters.Cost ?? DBNull.Value);
        command.Parameters.AddWithValue("inkable", NpgsqlTypes.NpgsqlDbType.Boolean, (object?)filters.Inkable ?? DBNull.Value);
        command.Parameters.AddWithValue("rarity", NpgsqlTypes.NpgsqlDbType.Text, (object?)filters.Rarity ?? DBNull.Value);
        command.Parameters.AddWithValue("type", NpgsqlTypes.NpgsqlDbType.Text, (object?)filters.CardType ?? DBNull.Value);
    }
}
