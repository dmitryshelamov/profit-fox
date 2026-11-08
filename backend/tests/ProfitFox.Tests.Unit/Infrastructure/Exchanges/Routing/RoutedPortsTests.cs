using NSubstitute;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Routing;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Routing;

/// <summary>Decision 8: every method of a read port goes to the exchange of the category.</summary>
public class RoutedPortsTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
    private static readonly DateTimeOffset To = From.AddDays(1);

    private IMarketHistoryClient _bybitHistory = null!;
    private IMarketHistoryClient _tinvestHistory = null!;
    private RoutedMarketHistoryClient _history = null!;

    [SetUp]
    public void SetUp()
    {
        _bybitHistory = Substitute.For<IMarketHistoryClient>();
        _tinvestHistory = Substitute.For<IMarketHistoryClient>();
        _history = new RoutedMarketHistoryClient(_bybitHistory, _tinvestHistory);
    }

    [TestCase(MarketCategory.Spot, "BTCUSDT", false)]
    [TestCase(MarketCategory.Linear, "BTCUSDT", false)]
    [TestCase(MarketCategory.Moex, "SBER", true)]
    public async Task Candle_pages_come_from_the_exchange_of_the_category(MarketCategory category, string symbol, bool tinvest)
    {
        var instrument = new Instrument(category, symbol);
        IMarketHistoryClient expected = tinvest ? _tinvestHistory : _bybitHistory;
        expected.GetCandlePagesAsync(instrument, Interval.OneHour, From, To, Arg.Any<CancellationToken>()).Returns(Pages(Candle(From)));

        List<IReadOnlyList<Candle>> pages = await _history.GetCandlePagesAsync(instrument, Interval.OneHour, From, To, CancellationToken.None).ToListAsync();

        pages.Single().Single().OpenTime.ShouldBe(From);
        (tinvest ? _bybitHistory : _tinvestHistory).ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(MarketCategory.Spot, false)]
    [TestCase(MarketCategory.Linear, false)]
    [TestCase(MarketCategory.Moex, true)]
    public async Task Candles_once_last_price_and_existence_come_from_the_exchange_of_the_category(MarketCategory category, bool tinvest)
    {
        var instrument = new Instrument(category, "X");
        IMarketHistoryClient expected = tinvest ? _tinvestHistory : _bybitHistory;
        expected.GetCandlesOnceAsync(instrument, Interval.OneHour, From, To, Arg.Any<CancellationToken>()).Returns([Candle(From)]);
        expected.GetLastPriceAsync(instrument, Arg.Any<CancellationToken>()).Returns(305.5m);
        expected.InstrumentExistsAsync(instrument, Arg.Any<CancellationToken>()).Returns(true);

        (await _history.GetCandlesOnceAsync(instrument, Interval.OneHour, From, To, CancellationToken.None)).Count.ShouldBe(1);
        (await _history.GetLastPriceAsync(instrument, CancellationToken.None)).ShouldBe(305.5m);
        (await _history.InstrumentExistsAsync(instrument, CancellationToken.None)).ShouldBeTrue();

        (tinvest ? _bybitHistory : _tinvestHistory).ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(MarketCategory.Spot, false)]
    [TestCase(MarketCategory.Linear, false)]
    [TestCase(MarketCategory.Moex, true)]
    public async Task Last_prices_of_a_category_come_from_its_exchange(MarketCategory category, bool tinvest)
    {
        IMarketHistoryClient expected = tinvest ? _tinvestHistory : _bybitHistory;
        expected.GetLastPricesAsync(category, Arg.Any<CancellationToken>()).Returns(new Dictionary<string, decimal> { ["X"] = 1m });

        (await _history.GetLastPricesAsync(category, CancellationToken.None)).ShouldContainKey("X");

        (tinvest ? _bybitHistory : _tinvestHistory).ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(MarketCategory.Spot, false)]
    [TestCase(MarketCategory.Linear, false)]
    [TestCase(MarketCategory.Moex, true)]
    public async Task Rules_come_from_the_exchange_of_the_category(MarketCategory category, bool tinvest)
    {
        IInstrumentRulesProvider bybit = Substitute.For<IInstrumentRulesProvider>();
        IInstrumentRulesProvider tinvestRules = Substitute.For<IInstrumentRulesProvider>();
        var instrument = new Instrument(category, "X");
        var rules = new InstrumentRules(0.01m, 10m, 10m, 0m);
        (tinvest ? tinvestRules : bybit).GetRulesAsync(instrument, Arg.Any<CancellationToken>()).Returns(rules);

        (await new RoutedInstrumentRulesProvider(bybit, tinvestRules).GetRulesAsync(instrument, CancellationToken.None)).ShouldBe(rules);

        (tinvest ? bybit : tinvestRules).ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase(MarketCategory.Spot, false)]
    [TestCase(MarketCategory.Linear, false)]
    [TestCase(MarketCategory.Moex, true)]
    public async Task Catalog_comes_from_the_exchange_of_the_category(MarketCategory category, bool tinvest)
    {
        IInstrumentCatalog bybit = Substitute.For<IInstrumentCatalog>();
        IInstrumentCatalog tinvestCatalog = Substitute.For<IInstrumentCatalog>();
        ListedInstrument[] listed = [new ListedInstrument(category, "X", "X", "Q")];
        (tinvest ? tinvestCatalog : bybit).ListAsync(category, Arg.Any<CancellationToken>()).Returns(listed);

        (await new RoutedInstrumentCatalog(bybit, tinvestCatalog).ListAsync(category, CancellationToken.None)).ShouldBe(listed);

        (tinvest ? bybit : tinvestCatalog).ReceivedCalls().ShouldBeEmpty();
    }

    private static async IAsyncEnumerable<IReadOnlyList<Candle>> Pages(params Candle[] candles)
    {
        await Task.Yield();

        yield return candles;
    }

    private static Candle Candle(DateTimeOffset open)
    {
        return new Candle(open, new Price(1m), new Price(1m), new Price(1m), new Price(1m), new Volume(1m), new Volume(1m));
    }
}