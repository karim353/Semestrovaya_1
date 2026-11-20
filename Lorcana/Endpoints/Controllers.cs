using System.Text.RegularExpressions;
using Lorcana.App.Data;
using Lorcana.Engine;
using Npgsql;

namespace Lorcana.App;

[HttpController("cards")]
internal sealed class CardsController(CatalogRepository catalog, Views views)
{
    private static readonly string[] Inks = ["Amber", "Amethyst", "Emerald", "Ruby", "Sapphire", "Steel", "Colorless"];
    internal static readonly string[] CardTypes = ["characters", "actions", "items", "locations", "supplies"];

    [HttpGet("/")]
    [HttpGet("/cards")]
    public async Task<IHttpResult> Index(Request request, string q = "", string? ink = null,
        int? cost = null, bool? inkable = null, int page = 1, string? rarity = null,
        string? cardType = null, string sort = "new")
    {
        var filters = Validate(q, ink, cost, inkable, page, rarity, cardType, sort);
        var results = await catalog.SearchAsync(filters, request.Cancellation);
        var options = await catalog.OptionsAsync(request.Cancellation);
        return await views.PageAsync("ListCards/index", new CatalogViewModel(results, filters, options),
            request.Cancellation);
    }

    [HttpGet("/api/cards")]
    [HttpGet("/api/cards/search")]
    public async Task<IHttpResult> Search(Request request, string q = "", string? ink = null,
        int? cost = null, bool? inkable = null, int page = 1, string? rarity = null,
        string? cardType = null, string sort = "new") =>
        HttpResult.Json(await catalog.SearchAsync(Validate(q, ink, cost, inkable, page, rarity, cardType, sort),
            request.Cancellation));

    [HttpGet("/cards/detail")]
    public async Task<IHttpResult> Detail(Request request, int id)
    {
        var detail = await catalog.DetailAsync(id, request.Cancellation);
        if (detail is null)
            return HttpResult.Error(404, "Карта не найдена.");
        return await views.PageAsync("CardEndpoint/index", new
        {
            detail.Card,
            detail.Flavor,
            detail.Illustrator,
            HasDescription = detail.DescriptionHtml.Length > 0,
            HasFlavor = detail.Flavor.Length > 0,
            HasIllustrator = detail.Illustrator.Length > 0,
            Description = new SafeHtml(Authentication.CleanDescription(detail.DescriptionHtml))
        }, request.Cancellation);
    }

    [HttpGet("/api/cards/detail")]
    public async Task<IHttpResult> DetailJson(Request request, int id)
    {
        var detail = await catalog.DetailAsync(id, request.Cancellation);
        return detail is null ? HttpResult.Error(404, "Карта не найдена.") :
            HttpResult.Json(detail with { DescriptionHtml = Authentication.CleanDescription(detail.DescriptionHtml) });
    }

    [HttpGet("/api/cards/options")]
    public async Task<IHttpResult> Options(Request request) =>
        HttpResult.Json(await catalog.OptionsAsync(request.Cancellation));

    private static Filters Validate(string q, string? ink, int? cost, bool? inkable, int page,
        string? rarity, string? cardType, string sort)
    {
        if (q.Length > 100 || q.Any(char.IsControl) || cost is < 0 or > 9 || page is < 1 or > 10000)
            throw new HttpProblem(400, "Некорректные параметры фильтра.");
        if (ink is not null && !Inks.Contains(ink, StringComparer.OrdinalIgnoreCase))
            throw new HttpProblem(400, "Неизвестный цвет чернил.");
        if (rarity?.Length > 40 || rarity?.Any(char.IsControl) == true ||
            (cardType is not null && !CardTypes.Contains(cardType)) || sort is not ("new" or "name" or "cost"))
            throw new HttpProblem(400, "Некорректная редкость, тип карты или сортировка.");
        return new(q.Trim(), ink, cost, inkable, page, rarity, cardType, sort);
    }
}

[HttpController("auth")]
internal sealed class AuthController(Authentication authentication, Views views)
{
    [HttpGet]
    public Task<IHttpResult> Login(Request request) =>
        views.PageAsync("AdminEndpoint/index", new { Title = "Вход" }, request.Cancellation);

    [HttpPost]
    public Task<IHttpResult> Login(Request request, string login, string password) =>
        authentication.LoginAsync(request, login, password);

    [HttpPost]
    public Task<IHttpResult> Logout(Request request) => authentication.LogoutAsync(request);
}

[HttpController("admin")]
internal sealed class AdminController(Authentication authentication, CatalogRepository catalog, Views views)
{
    private static readonly Regex CardCode = new(@"\A[A-Z0-9]{2,8}-[0-9]{1,4}\z");
    private static readonly Regex CardName = new(@"\A[\p{L}\p{M}\p{N}\p{P}\p{S} ]{2,120}\z");

    [HttpGet("/admin")]
    public async Task<IHttpResult> Index(Request request, int page = 1, string q = "")
    {
        var session = authentication.RequireAdmin(request);
        if (page is < 1 or > 10000 || q.Length > 100 || q.Any(char.IsControl))
            return HttpResult.Error(400, "Некорректные параметры списка.");
        var results = await catalog.SearchAsync(new(q.Trim(), null, null, null, page), request.Cancellation);
        var options = await catalog.OptionsAsync(request.Cancellation);
        return await views.PageAsync("AdminEndpoint/panel", new
        {
            session.Login, session.Csrf, Results = results, options.Inks, options.Sets, options.Rarities,
            Query = q,
            PreviousUrl = $"/admin?page={Math.Max(1, page - 1)}&q={Uri.EscapeDataString(q)}",
            NextUrl = $"/admin?page={page + 1}&q={Uri.EscapeDataString(q)}"
        }, request.Cancellation);
    }

    [HttpGet("/api/admin/session")]
    public Task<IHttpResult> Session(Request request)
    {
        var session = authentication.RequireAdmin(request);
        return Task.FromResult<IHttpResult>(HttpResult.Json(new { session.Login, session.Role, session.Csrf }));
    }

    [HttpGet("/api/admin/card")]
    public async Task<IHttpResult> ReadCard(Request request, int id)
    {
        authentication.RequireAdmin(request);
        var card = await catalog.Cards.ReadByIdAsync(id, request.Cancellation);
        if (card is null)
            return HttpResult.Error(404, "Карта не найдена.");
        card.DescriptionHtml = Authentication.CleanDescription(card.DescriptionHtml);
        return HttpResult.Json(card);
    }

    [HttpPost("/api/admin/cards/create")]
    public async Task<IHttpResult> Create(Request request, string name, string code, int inkId,
        int setId, int rarityId, int cost, bool inkable = false, string imageUrl = "", string descriptionHtml = "",
        int? secondInkId = null, string identifier = "", string cardType = "", string flavor = "", string illustrator = "")
    {
        await authentication.AuthorizeWriteAsync(request);
        var card = Validate(name, code, inkId, setId, rarityId, cost, inkable, imageUrl, descriptionHtml);
        ApplyMetadata(card, secondInkId, identifier, cardType, flavor, illustrator);
        int id = await catalog.Cards.CreateAsync(card, request.Cancellation);
        return HttpResult.Json(new { id, url = $"/cards/detail/{id}" }, 201);
    }

    [HttpPost("/api/admin/cards/update")]
    public async Task<IHttpResult> Update(Request request, int id, string name, string code, int inkId,
        int setId, int rarityId, int cost, bool inkable = false, string imageUrl = "", string descriptionHtml = "",
        int? secondInkId = null, string identifier = "", string cardType = "", string flavor = "", string illustrator = "")
    {
        await authentication.AuthorizeWriteAsync(request);
        var card = Validate(name, code, inkId, setId, rarityId, cost, inkable, imageUrl, descriptionHtml);
        ApplyMetadata(card, secondInkId, identifier, cardType, flavor, illustrator);
        return await catalog.Cards.UpdateAsync(id, card, request.Cancellation)
            ? HttpResult.Json(new { id }) : HttpResult.Error(404, "Карта не найдена.");
    }

    [HttpPost("/api/admin/cards/delete")]
    public async Task<IHttpResult> Delete(Request request, int id)
    {
        await authentication.AuthorizeWriteAsync(request);
        return await catalog.Cards.DeleteAsync(id, request.Cancellation)
            ? HttpResult.Json(new { id }) : HttpResult.Error(404, "Карта не найдена.");
    }

    private static Card Validate(string name, string code, int inkId, int setId, int rarityId,
        int cost, bool inkable, string imageUrl, string descriptionHtml)
    {
        name = name.Trim();
        code = code.Trim().ToUpperInvariant();
        if (!CardName.IsMatch(name) || !CardCode.IsMatch(code))
            throw new HttpProblem(400, "Проверьте название и код карты, например TFC-123.");
        if (inkId < 1 || setId < 1 || rarityId < 1 || cost is < 0 or > 99 || descriptionHtml.Length > 8000)
            throw new HttpProblem(400, "Некорректные параметры карты.");
        if (!ValidImage(imageUrl))
            throw new HttpProblem(400, "Изображение должно иметь HTTPS-адрес или путь /assets/.");
        return new Card
        {
            Name = name, Code = code, InkId = inkId, SetId = setId, RarityId = rarityId, Cost = cost,
            Inkable = inkable, ImageUrl = imageUrl, DescriptionHtml = Authentication.CleanDescription(descriptionHtml)
        };
    }

    private static bool ValidImage(string url)
    {
        if (url.Length == 0)
            return true;
        if (url.Length > 500 || url.Any(char.IsControl) || url.Contains('\\'))
            return false;
        return url.StartsWith("/assets/", StringComparison.Ordinal) ||
            (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host.Length > 0 && uri.UserInfo.Length == 0);
    }

    private static void ApplyMetadata(Card card, int? secondInkId, string identifier, string cardType,
        string flavor, string illustrator)
    {
        if (secondInkId is < 1 || secondInkId == card.InkId || identifier.Length > 40 ||
            (cardType.Length > 0 && !CardsController.CardTypes.Contains(cardType)) ||
            flavor.Length > 1000 || illustrator.Length > 120)
            throw new HttpProblem(400, "Проверьте дополнительные сведения о карте.");
        card.SecondInkId = secondInkId;
        card.Identifier = identifier.Trim();
        card.CardType = cardType;
        card.Flavor = flavor.Trim();
        card.Illustrator = illustrator.Trim();
    }
}

[HttpController("feedback")]
internal sealed class FeedbackController(Orm<Feedback> feedback, Views views)
{
    private static readonly Regex NamePattern = new(@"\A[\p{L}][\p{L}\p{M} .'-]{1,59}\z");
    private static readonly Regex EmailPattern = new(@"\A[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,63}\z");
    private static readonly string[] Topics = ["catalog", "error", "idea", "other"];

    [HttpGet("/feedback")]
    public Task<IHttpResult> Index(Request request) =>
        views.PageAsync("FeedbackEndpoint/index", new { Title = "Обратная связь" }, request.Cancellation);

    [HttpPost("/api/feedback")]
    public async Task<IHttpResult> Submit(Request request, string name, string email, string topic, string message)
    {
        Authentication.RequireSameOrigin(request);
        name = name.Trim();
        email = email.Trim();
        message = message.Trim();
        if (name.Length > 60 || !NamePattern.IsMatch(name))
            throw new HttpProblem(400, "Укажите имя из 2–60 букв.");
        if (email.Length > 254 || !EmailPattern.IsMatch(email))
            throw new HttpProblem(400, "Укажите корректный e-mail.");
        if (!Topics.Contains(topic) || message.Length is < 5 or > 1000)
            throw new HttpProblem(400, "Выберите тему и введите от 5 до 1000 символов.");
        int id = await feedback.CreateAsync(new Feedback { Name = name, Email = email, Topic = topic, Message = message },
            request.Cancellation);
        return HttpResult.Json(new { id }, 201);
    }
}

[HttpController("health")]
internal sealed class HealthController(NpgsqlDataSource data)
{
    [HttpGet("/health")]
    public async Task<IHttpResult> Check(Request request)
    {
        await using var command = data.CreateCommand("SELECT 1 FROM cards LIMIT 1");
        await command.ExecuteScalarAsync(request.Cancellation);
        return HttpResult.Json(new { status = "ok", database = "ready" });
    }
}
