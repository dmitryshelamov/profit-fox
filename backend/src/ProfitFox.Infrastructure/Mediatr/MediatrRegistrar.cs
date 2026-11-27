using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.EnqueueBacktest;

namespace ProfitFox.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    /// <summary>
    ///     The handlers of the commands and queries of Application. No request runs in a transaction of the pipeline: a repository
    ///     makes each of its calls whole in a transaction of its own.
    /// </summary>
    internal static void Configure(IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(EnqueueBacktestCommand).Assembly));
    }
}
