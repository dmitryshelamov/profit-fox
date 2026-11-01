using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.Backtests.Commands.RequeueInterruptedBacktests;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Queues again the backtests the previous process left unfinished, before the server takes requests.</summary>
internal sealed class BacktestInitializationService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RequeueInterruptedBacktestsCommand(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}