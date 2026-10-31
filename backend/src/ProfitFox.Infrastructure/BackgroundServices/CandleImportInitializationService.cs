using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.MarketData.Commands.RequeueInterruptedImports;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     Queues again the jobs the previous process left unfinished. It is an <see cref="IHostedService" />, not a <see cref="BackgroundService" />:
///     the host awaits <see cref="StartAsync" /> before the server takes requests, so the interrupted jobs keep their place before new ones.
/// </summary>
internal sealed class CandleImportInitializationService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RequeueInterruptedImportsCommand(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}