using ProfitFox.Application.MarketData.Queries.SearchInstruments;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class InstrumentSearchTests
{
    private static readonly ListedInstrument[] Spot =
    [
        Listed("STETHUSDT", "STETH", "USDT"),
        Listed("ETHUSDT", "ETH", "USDT"),
        Listed("BTCUSDT", "BTC", "USDT"),
        Listed("ETHBTC", "ETH", "BTC"),
        Listed("SOLUSDC", "SOL", "USDC")
    ];

    // Scenario "Часть символа".
    [Test]
    public void Matches_a_part_of_the_symbol_ignoring_case()
    {
        IReadOnlyList<ListedInstrument> found = InstrumentSearch.Find(Spot, "btc", limit: 20);

        found.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDT", "ETHBTC"]);
        found.First().ShouldBe(Listed("BTCUSDT", "BTC", "USDT"));
    }

    [Test]
    public void Matches_the_quote_coin_which_is_not_in_the_symbol_start()
    {
        InstrumentSearch.Find(Spot, "usdc", limit: 20).Select(instrument => instrument.Symbol).ShouldBe(["SOLUSDC"]);
    }

    [Test]
    public void Matches_a_coin_that_the_symbol_does_not_contain()
    {
        InstrumentSearch.Find([Listed("XBTUSD", "BTC", "USD")], "btc", limit: 20).Count.ShouldBe(1);
    }

    // Scenario "Ничего не найдено".
    [Test]
    public void Nothing_matching_gives_an_empty_list()
    {
        InstrumentSearch.Find(Spot, "NOPE", limit: 20).ShouldBeEmpty();
    }

    // Scenario "Сначала начинающиеся с запроса".
    [Test]
    public void Symbols_starting_with_the_query_go_first_each_part_ordered_by_symbol()
    {
        InstrumentSearch.Find(Spot, "ETH", limit: 20).Select(instrument => instrument.Symbol).ShouldBe(["ETHBTC", "ETHUSDT", "STETHUSDT"]);
    }

    [Test]
    public void Order_is_ordinal_not_cultural()
    {
        ListedInstrument[] instruments = [Listed("BTC_B", "B", "C"), Listed("BTCA", "A", "C"), Listed("BTC1", "A", "C")];

        InstrumentSearch.Find(instruments, "btc", limit: 20).Select(instrument => instrument.Symbol).ShouldBe(["BTC1", "BTCA", "BTC_B"]);
    }

    [Test]
    public void Result_is_cut_to_the_limit_after_ordering()
    {
        InstrumentSearch.Find(Spot, "ETH", limit: 2).Select(instrument => instrument.Symbol).ShouldBe(["ETHBTC", "ETHUSDT"]);
    }

    private static ListedInstrument Listed(string symbol, string baseCoin, string quoteCoin)
    {
        return new ListedInstrument(MarketCategory.Spot, symbol, baseCoin, quoteCoin);
    }
}