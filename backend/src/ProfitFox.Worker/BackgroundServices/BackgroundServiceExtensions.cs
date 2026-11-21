using MediatR;

namespace ProfitFox.Worker.BackgroundServices;

/// <summary>
///     What a background service of the Worker does the same way as those of profit-fox-market-data.
/// </summary>
internal static class BackgroundServiceExtensions
{
    /// <summary>
    ///     Sends the request in a scope of its own, so it shares nothing with the other requests.
    /// </summary>
    public static async Task<TResponse> SendAsync<TResponse>(this IServiceScopeFactory scopes, IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }
}
