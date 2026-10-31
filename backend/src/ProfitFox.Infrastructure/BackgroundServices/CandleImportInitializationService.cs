using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.MarketData.Commands.FailInterruptedImports;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
/// Fails jobs the previous process left unfinished. It is an <see cref="IHostedService"/>, not a <see cref="BackgroundService"/>:
/// the host awaits <see cref="StartAsync"/> before the server takes requests, so a newly created job is never failed by mistake.
/// </summary>
internal sealed class CandleImportInitializationService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailInterruptedImportsCommand(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
