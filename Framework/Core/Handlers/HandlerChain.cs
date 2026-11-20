namespace Lorcana.Engine;

public sealed class HandlerChain(params IRequestHandler[] handlers)
{
    public async Task<IHttpResult> HandleAsync(Request request)
    {
        foreach (var handler in handlers)
        {
            var response = await handler.HandleAsync(request);
            if (response is not null)
                return response;
        }
        return HttpResult.Error(404, "Страница не найдена.");
    }
}
