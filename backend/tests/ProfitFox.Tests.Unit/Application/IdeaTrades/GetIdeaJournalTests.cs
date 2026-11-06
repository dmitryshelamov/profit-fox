using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>Requirements "Чтение журнала", "Фильтры журнала" and "Текущий результат сделки в работе": the open trades, a page of the closed ones, the totals.</summary>
public class GetIdeaJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly GetIdeaJournalQuery Everything = new GetIdeaJournalQuery(null, null, null, null, null, null, null);
    private static readonly Instrument Eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");

    private readonly Guid _account = Guid.NewGuid();
    private IIdeaTradeRepository _trades = null!;
    private IMarketHistoryClient _history = null!;
    private GetIdeaJournalQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _trades = Substitute.For<IIdeaTradeRepository>();
        _history = Substitute.For<IMarketHistoryClient>();
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ExchangeAccount(_account, "Основной", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Noon)]);
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };
        _handler = new GetIdeaJournalQueryHandler(_trades, accounts, _history, backtests, new FakeTimeProvider(Noon));
    }

    // Scenario "Сделки в работе и закрытые": two open trades and 120 closed ones, the second page of 50.
    [Test]
    public async Task Gives_the_open_trades_newest_first_and_the_second_page_of_the_closed_ones_by_the_time_they_closed()
    {
        IdeaTrade older = Waiting(Noon.AddHours(-2));
        IdeaTrade newer = Waiting(Noon.AddHours(-1));
        IdeaTrade[] closed = [.. Enumerable.Range(0, 120).Select(minutes => Closed(Noon.AddMinutes(-minutes)))];
        Trades([older, .. closed.Reverse(), newer]);

        GetIdeaJournalResult result = await _handler.Handle(Everything with { Page = 2 }, CancellationToken.None);

        result.Journal!.Open.Select(item => item.Trade.Id).ShouldBe([newer.Id, older.Id]);
        result.Journal.Open[0].AccountName.ShouldBe("Основной");
        result.Journal.Closed.Items.Select(item => item.Trade.Id).ShouldBe([.. closed[50..100].Select(trade => trade.Id)]);
        (result.Journal.Closed.Number, result.Journal.Closed.Size, result.Journal.Closed.Total).ShouldBe((2, 50, 120));
    }

    // The totals are over every closed trade of the filter, not over the page.
    [Test]
    public async Task Totals_count_all_the_closed_trades_of_the_filter_and_not_the_page()
    {
        Trades([.. Enumerable.Range(0, 60).Select(minutes => Closed(Noon.AddMinutes(-minutes)))]);

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Journal!.Closed.Items.Count.ShouldBe(50);
        result.Journal.Totals.Targets.ShouldBe(60);
    }

    [Test]
    public async Task Cancelled_and_failed_trades_are_among_the_closed_ones()
    {
        IdeaTrade cancelled = Waiting(Noon.AddHours(-3)).EntryEnded(Noon.AddHours(-2));
        IdeaTrade failed = Waiting(Noon.AddHours(-3)).Fail("refused", Noon.AddHours(-1));
        Trades([cancelled, failed]);

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Journal!.Open.ShouldBeEmpty();
        result.Journal.Closed.Items.Select(item => item.Trade.Id).ShouldBe([failed.Id, cancelled.Id]);
        (result.Journal.Totals.Cancelled, result.Journal.Totals.Failed).ShouldBe((1, 1));
    }

    // Scenario "Неизвестный размер страницы" and the unknown codes: the errors are named by the parameter.
    [Test]
    public async Task Wrong_parameters_are_errors_named_by_the_parameter()
    {
        GetIdeaJournalResult result = await _handler.Handle(new GetIdeaJournalQuery("nothing", null, "up", "sideways", 14, 0, 75), CancellationToken.None);

        result.Journal.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["setup", "side", "badge", "days", "page", "pageSize"], ignoreOrder: true);
    }

    // Scenario "Только ▲ за 30 дней": the period is of the time of the close and does not touch the open trades; the badge does.
    [Test]
    public async Task Badge_and_period_filter_the_closed_trades_and_the_badge_the_open_ones()
    {
        IdeaTrade upRecent = Closed(Noon.AddDays(-10), badge: IdeaBadge.Up);
        IdeaTrade upOld = Closed(Noon.AddDays(-40), badge: IdeaBadge.Up);
        IdeaTrade flatRecent = Closed(Noon.AddDays(-10), badge: IdeaBadge.Flat);
        IdeaTrade upOpen = Waiting(Noon.AddDays(-45), badge: IdeaBadge.Up);
        IdeaTrade flatOpen = Waiting(Noon.AddDays(-1), badge: IdeaBadge.Flat);
        Trades([upRecent, upOld, flatRecent, upOpen, flatOpen]);

        GetIdeaJournalResult result = await _handler.Handle(Everything with { Badge = "up", Days = 30 }, CancellationToken.None);

        result.Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([upRecent.Id]);
        result.Journal.Open.Select(item => item.Trade.Id).ShouldBe([upOpen.Id]);
        result.Journal.Totals.Targets.ShouldBe(1);
    }

    [Test]
    public async Task Period_takes_the_trades_closed_within_the_days_and_all_time_takes_every_one()
    {
        IdeaTrade inside = Closed(Noon.AddDays(-7));
        IdeaTrade outside = Closed(Noon.AddDays(-7).AddMinutes(-1));
        Trades([inside, outside]);

        (await _handler.Handle(Everything with { Days = 7 }, CancellationToken.None)).Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([inside.Id]);
        (await _handler.Handle(Everything with { Days = 90 }, CancellationToken.None)).Journal!.Closed.Total.ShouldBe(2);
        (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Closed.Total.ShouldBe(2);
    }

    // Scenario "Без значка": a promise with too few cases and a trade with no promise.
    [Test]
    public async Task No_badge_takes_the_trades_with_too_few_cases_and_those_with_no_promise()
    {
        IdeaTrade few = Closed(Noon.AddHours(-1)) with { Promise = new IdeaPromise(null, null, 0.05m, 12, 0.35m, 1.8m) };
        IdeaTrade none = Closed(Noon.AddHours(-2)) with { Promise = null };
        IdeaTrade up = Closed(Noon.AddHours(-3), badge: IdeaBadge.Up);
        Trades([few, none, up]);

        GetIdeaJournalResult result = await _handler.Handle(Everything with { Badge = "none" }, CancellationToken.None);

        result.Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([few.Id, none.Id]);
    }

    // Scenario "Пара": a part of the symbol, whatever its case, for both groups.
    [Test]
    public async Task Pair_is_a_part_of_the_symbol_in_any_case()
    {
        IdeaTrade ethClosed = Closed(Noon.AddHours(-1), instrument: Eth);
        IdeaTrade btcClosed = Closed(Noon.AddHours(-2), instrument: Btc);
        IdeaTrade ethOpen = Waiting(Noon.AddHours(-1), instrument: Eth);
        IdeaTrade btcOpen = Waiting(Noon.AddHours(-1), instrument: Btc);
        Trades([ethClosed, btcClosed, ethOpen, btcOpen]);

        GetIdeaJournalResult result = await _handler.Handle(Everything with { Symbol = "eth" }, CancellationToken.None);

        result.Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([ethClosed.Id]);
        result.Journal.Open.Select(item => item.Trade.Id).ShouldBe([ethOpen.Id]);
    }

    [Test]
    public async Task Setup_and_side_filter_both_groups()
    {
        IdeaTrade impulseLong = Closed(Noon.AddHours(-1));
        IdeaTrade dropLong = Closed(Noon.AddHours(-2), setup: IdeaSetup.Drop);
        IdeaTrade impulseShort = Closed(Noon.AddHours(-3), side: IdeaSide.Short);
        IdeaTrade openShort = Waiting(Noon.AddHours(-1), side: IdeaSide.Short);
        Trades([impulseLong, dropLong, impulseShort, openShort]);

        GetIdeaJournalResult setup = await _handler.Handle(Everything with { Setup = "drop" }, CancellationToken.None);
        GetIdeaJournalResult side = await _handler.Handle(Everything with { Side = "short" }, CancellationToken.None);

        setup.Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([dropLong.Id]);
        setup.Journal.Open.ShouldBeEmpty();
        side.Journal!.Closed.Items.Select(item => item.Trade.Id).ShouldBe([impulseShort.Id]);
        side.Journal.Open.Select(item => item.Trade.Id).ShouldBe([openShort.Id]);
    }

    // Scenario "Открытая в плюсе": 0.47 bought at 23.48 (11.0356, the fee 0.0061), the last price 23.90; the exit pays the taker fee of 0.055 %.
    [Test]
    public async Task Open_trade_is_worth_the_rest_closed_at_the_last_price()
    {
        IdeaTrade open = Opened(Noon.AddHours(-1), Eth);
        Trades([open]);
        _history.GetLastPriceAsync(Eth, Arg.Any<CancellationToken>()).Returns(23.90m);

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        IdeaJournalTrade item = result.Journal!.Open.Single();
        decimal expected = 11.233m - 11.0356m - 0.0061m - (11.233m * 0.00055m);
        (item.LastPrice, item.CurrentResult, item.CurrentResultR).ShouldBe((23.90m, expected, expected / 0.2m));
    }

    // One request for the price of a symbol, however many trades are open on it; a trade that waits for its entry has no result and asks for nothing.
    [Test]
    public async Task Last_price_is_asked_once_for_a_symbol_and_not_for_a_trade_that_waits()
    {
        Trades([Opened(Noon.AddHours(-1), Eth), Opened(Noon.AddHours(-2), Eth), Waiting(Noon.AddHours(-3), instrument: Btc)]);
        _history.GetLastPriceAsync(Eth, Arg.Any<CancellationToken>()).Returns(23.90m);

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        await _history.Received(1).GetLastPriceAsync(Eth, Arg.Any<CancellationToken>());
        await _history.DidNotReceive().GetLastPriceAsync(Btc, Arg.Any<CancellationToken>());
        result.Journal!.Open.Select(item => item.CurrentResult is not null).ShouldBe([true, true, false]);
    }

    // Scenario "Bybit не ответил": the journal answers without an error, the trade has no current result.
    [Test]
    public async Task Exchange_that_gives_no_price_leaves_the_trade_without_a_current_result()
    {
        Trades([Opened(Noon.AddHours(-1), Eth), Opened(Noon.AddHours(-2), Btc)]);
        _history.GetLastPriceAsync(Eth, Arg.Any<CancellationToken>()).Returns<decimal>(_ => throw new ExchangeUnavailableException("no ticker"));
        _history.GetLastPriceAsync(Btc, Arg.Any<CancellationToken>()).Returns(23.90m);

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        IdeaJournalTrade failed = result.Journal!.Open[0];
        (failed.LastPrice, failed.CurrentResult, failed.CurrentResultR).ShouldBe((null, null, null));
        result.Journal.Open[1].CurrentResult.ShouldNotBeNull();
    }

    private IdeaTrade Opened(DateTimeOffset createdAt, Instrument instrument)
    {
        return Waiting(createdAt, instrument: instrument).EntryGrown(new LegTotals(0.47m, 11.0356m, 0.0061m), createdAt.AddMinutes(1)) with { Risk = 0.2m };
    }

    private void Trades(IEnumerable<IdeaTrade> trades)
    {
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([.. trades]);
    }

    private IdeaTrade Waiting(DateTimeOffset createdAt, IdeaBadge? badge = null, Instrument? instrument = null, IdeaSide side = IdeaSide.Long)
    {
        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(createdAt, instrument: instrument, side: side), _account, 100m, Size(), createdAt);

        return trade with { Promise = badge is null ? null : new IdeaPromise(badge, 0.3m, 0.1m, 400, 0.35m, 1.8m) };
    }

    private IdeaTrade Closed(DateTimeOffset closedAt, IdeaBadge? badge = null, Instrument? instrument = null, IdeaSetup setup = IdeaSetup.Impulse, IdeaSide side = IdeaSide.Long)
    {
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(closedAt.AddHours(-3), instrument: instrument, setup: setup, side: side), _account, 100m, Size(), closedAt.AddHours(-3))
            .EntryGrown(new LegTotals(1m, 100m, 0m), closedAt.AddHours(-2))
            .ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(1m, 104m, 0m), closedAt);

        return trade with { Promise = badge is null ? null : new IdeaPromise(badge, 0.3m, 0.1m, 400, 0.35m, 1.8m) };
    }

    private static IdeaTradeSize Size()
    {
        return new IdeaTradeSize(1m, 100m, 2m, 4m, 2m);
    }
}