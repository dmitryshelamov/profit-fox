using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Integration.Workers;

using Shouldly;

namespace ProfitFox.Tests.Integration.Scanner;

/// <summary>Requirement "Сканер — отдельный процесс".</summary>
public class ScannerIsolationTests
{
    // Scenario "Падение сканера не трогает ботов".
    [Test]
    public async Task Bot_goes_on_trading_while_the_scanner_fails_and_stops()
    {
        await using LiveTradingScenario scenario = await LiveTradingScenario.CreateAsync();
        Guid bot = await scenario.CreateBotAsync();
        scenario.StartWorker();
        await scenario.ActAsync(bot, "start");
        await scenario.WaitAsync(async () => (await scenario.OrdersAsync(bot)).SingleOrDefault()?.Status == LiveOrderStatus.Active, "the start order on the exchange");
        IMarketStreams failing = Substitute.For<IMarketStreams>();
        failing.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Domain.Entities.MarketData.Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The scanner breaks."));
        await using FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        var scanner = new ScannerHost(scenario.Factory.ConnectionString, new FakeBybitHandler(), sockets, scenario.Factory.Time, services: services => services.AddSingleton(failing));

        await scanner.StartAsync();
        await scenario.WaitAsync(() => failing.ReceivedCalls().Any(), "the scanner failing");
        await scanner.DisposeAsync();
        scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);

        await scenario.WaitAsync(async () => (await scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the fill in the position");
        (await scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
        scenario.Exchange.Sent.Count.ShouldBe(1);
    }
}