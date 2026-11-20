using Lorcana.App.Data;

namespace Lorcana.App;

internal sealed class CatalogViewModel(CatalogPage results, Filters filters, CatalogOptions options)
{
    public CatalogPage Results { get; } = results;
    public Filters Filters { get; } = filters;
    public bool IsEmpty => Results.Cards.Count == 0;
    public string PreviousUrl => PageUrl(Math.Max(1, Results.Page - 1));
    public string NextUrl => PageUrl(Results.Page + 1);

    public IEnumerable<Option> InkChoices => Choices(
        new[] { ("", "Все") }.Concat(options.Inks.Select(ink =>
            (ink.Name, ink.Name == "Colorless" ? "Без цвета" : ink.Name))), Filters.Ink);

    public IEnumerable<Option> InkableChoices => Choices(
        [("", "Все"), ("true", "Да"), ("false", "Нет")], Filters.Inkable?.ToString());

    public IEnumerable<Option> CostChoices => Choices(
        new[] { ("", "Все") }.Concat(Enumerable.Range(0, 10)
            .Select(cost => (cost.ToString(), cost == 9 ? "9+" : cost.ToString()))), Filters.Cost?.ToString());

    public IEnumerable<Option> RarityChoices => Choices(
        new[] { ("", "Все") }.Concat(options.Rarities.Select(rarity => (rarity.Name, rarity.Name))), Filters.Rarity);

    public IEnumerable<Option> TypeChoices => Choices(
        [("", "Все"), ("characters", "Персонаж"), ("actions", "Действие"),
         ("items", "Предмет"), ("locations", "Локация"), ("supplies", "Припасы")], Filters.CardType);

    public IEnumerable<Option> SortChoices => Choices(
        [("new", "Новые первыми"), ("name", "По названию"), ("cost", "По стоимости")], Filters.Sort);

    public sealed record Option(string Value, string Label, bool Selected)
    {
        public bool HasValue => Value.Length > 0;
    }

    private static IEnumerable<Option> Choices(IEnumerable<(string Value, string Label)> choices, string? selected) =>
        choices.Select(choice => new Option(choice.Value, choice.Label,
            string.Equals(choice.Value, selected ?? "", StringComparison.OrdinalIgnoreCase)));

    private string PageUrl(int page)
    {
        (string Key, string? Value)[] values =
        [
            ("q", Filters.Q), ("ink", Filters.Ink), ("cost", Filters.Cost?.ToString()),
            ("inkable", Filters.Inkable?.ToString()?.ToLowerInvariant()), ("rarity", Filters.Rarity),
            ("cardType", Filters.CardType), ("sort", Filters.Sort == "new" ? null : Filters.Sort),
            ("page", page == 1 ? null : page.ToString())
        ];
        var query = string.Join("&", values.Where(value => !string.IsNullOrEmpty(value.Value))
            .Select(value => $"{value.Key}={Uri.EscapeDataString(value.Value!)}"));
        return query.Length == 0 ? "/" : "/cards?" + query;
    }
}
