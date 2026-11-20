namespace Lorcana.App.Data;

[Table("cards")]
public sealed class Card
{
    [Column("id", key: true)] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("code")] public string Code { get; set; } = "";
    [Column("ink_id")] public int InkId { get; set; }
    [Column("second_ink_id")] public int? SecondInkId { get; set; }
    [Column("set_id")] public int SetId { get; set; }
    [Column("rarity_id")] public int RarityId { get; set; }
    [Column("cost")] public int Cost { get; set; }
    [Column("inkable")] public bool Inkable { get; set; }
    [Column("image_url")] public string ImageUrl { get; set; } = "";
    [Column("description_html")] public string DescriptionHtml { get; set; } = "";
    [Column("identifier")] public string Identifier { get; set; } = "";
    [Column("card_type")] public string CardType { get; set; } = "";
    [Column("flavor")] public string Flavor { get; set; } = "";
    [Column("illustrator")] public string Illustrator { get; set; } = "";
}

[Table("inks")]
public sealed class Ink
{
    [Column("id", key: true)] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("hex_color")] public string HexColor { get; set; } = "";
    [Column("display_order")] public int DisplayOrder { get; set; }
}

[Table("card_sets")]
public sealed class CardSet
{
    [Column("id", key: true)] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("slug")] public string Slug { get; set; } = "";
    [Column("description")] public string Description { get; set; } = "";
}

[Table("rarities")]
public sealed class Rarity
{
    [Column("id", key: true)] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("label")] public string Label { get; set; } = "";
    [Column("rank")] public int Rank { get; set; }
}

public sealed record CardSummary(int Id, string Name, string Code, string Ink, string Set, string Rarity,
    int Cost, bool Inkable, string ImageUrl, string Identifier, string CardType, string? SecondInk)
{
    public string Url => $"/cards/detail/{Id}";
    public bool HasImage => ImageUrl.Length > 0;
    public string InkLabel => SecondInk is null ? Ink : $"{Ink} / {SecondInk}";
}

public sealed record CardDetail(CardSummary Card, string DescriptionHtml, string Flavor, string Illustrator);
public sealed record CatalogPage(IReadOnlyList<CardSummary> Cards, int Page, int PageSize, long Total,
    bool HasPrevious, bool HasNext);
public sealed record Filters(string Q, string? Ink, int? Cost, bool? Inkable, int Page,
    string? Rarity = null, string? CardType = null, string Sort = "new")
{
    public const int PageSize = 24;
}

public sealed record CatalogOptions(IReadOnlyList<Ink> Inks, IReadOnlyList<CardSet> Sets, IReadOnlyList<Rarity> Rarities);

[Table("feedback")]
public sealed class Feedback
{
    [Column("id", key: true)] public int Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("email")] public string Email { get; set; } = "";
    [Column("topic")] public string Topic { get; set; } = "";
    [Column("message")] public string Message { get; set; } = "";
}
