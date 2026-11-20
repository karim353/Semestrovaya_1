namespace Lorcana.Engine;

public interface IRequestHandler
{
    Task<IHttpResult?> HandleAsync(Request request);
}
