using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>The streams of closed and of forming candles are one object, so a kline topic is shared between the API and a Worker's code path.</summary>
public class StreamsRegistrationTests
{
    [Test]
    public async Task Api_and_worker_resolve_both_stream_ports_to_one_singleton()
    {
        await using var factory = ProfitFoxFactory.Create(workers: 1);
        factory.CreateClient().Dispose();

        IServiceProvider[] hosts = [factory.Services, factory.WorkerHosts[0].Services];

        foreach (IServiceProvider services in hosts)
        {
            services.GetRequiredService<IFormingCandleStreams>().ShouldBeSameAs(services.GetRequiredService<IExchangeStreams>());
        }
    }
}