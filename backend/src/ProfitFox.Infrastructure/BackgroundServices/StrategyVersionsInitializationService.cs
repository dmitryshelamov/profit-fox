using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.Strategies.Commands.CreateMissingStrategyVersions;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Gives version 1 to strategies saved before versions existed; the host awaits it before the server takes requests.</summary>
internal sealed class StrategyVersionsInitializationService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateMissingStrategyVersionsCommand(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}