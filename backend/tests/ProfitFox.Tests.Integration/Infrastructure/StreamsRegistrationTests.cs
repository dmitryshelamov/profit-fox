using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Routing;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     Decisions 2 and 11: the ports of trading are the routers, over the implementations of Bybit and T-Invest keyed by the exchange; the
///     streams of closed and of forming candles of an exchange are one object, so a series is shared between the API and a Worker's code path.
/// </summary>
public class StreamsRegistrationTests
{
    [Test]
    public async Task Api_and_worker_route_the_streams_to_one_singleton_of_each_exchange()
    {
        await using var factory = ProfitFoxFactory.Create(workers: 1);
        factory.CreateClient().Dispose();

        IServiceProvider[] hosts = [factory.Services, factory.WorkerHosts[0].Services];

        foreach (IServiceProvider services in hosts)
        {
            services.GetRequiredService<IExchangeStreams>().ShouldBeOfType<RoutedExchangeStreams>();
            services.GetRequiredService<IFormingCandleStreams>().ShouldBeOfType<RoutedFormingCandleStreams>();
            services.GetRequiredKeyedService<IExchangeStreams>(Exchange.Bybit).ShouldBeOfType<BybitStreams>()
                .ShouldBeSameAs(services.GetRequiredKeyedService<IFormingCandleStreams>(Exchange.Bybit));
            services.GetRequiredKeyedService<IExchangeStreams>(Exchange.TInvest).ShouldBeOfType<TInvestStreams>()
                .ShouldBeSameAs(services.GetRequiredKeyedService<IFormingCandleStreams>(Exchange.TInvest));
        }
    }

    [Test]
    public async Task Worker_routes_the_orders_and_the_reads_of_an_account_by_the_exchange()
    {
        await using var factory = ProfitFoxFactory.Create(workers: 1);
        factory.CreateClient().Dispose();
        IServiceProvider services = factory.WorkerHosts[0].Services;

        services.GetRequiredService<IOrderGateway>().ShouldBeOfType<RoutedOrderGateway>();
        services.GetRequiredService<IExchangeLedger>().ShouldBeOfType<RoutedExchangeLedger>();
        services.GetRequiredKeyedService<IOrderGateway>(Exchange.Bybit).ShouldBeOfType<BybitOrderGateway>();
        services.GetRequiredKeyedService<IOrderGateway>(Exchange.TInvest).ShouldBeOfType<TInvestOrderGateway>();
        services.GetRequiredKeyedService<IExchangeLedger>(Exchange.Bybit).ShouldBeOfType<BybitLedger>();
        services.GetRequiredKeyedService<IExchangeLedger>(Exchange.TInvest).ShouldBeOfType<TInvestLedger>();
    }
}