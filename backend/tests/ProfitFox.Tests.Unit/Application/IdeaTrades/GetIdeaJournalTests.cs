using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
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

    private static readonly IdeaAuto Impulse = Auto("Импульс лонг", null);
    private static readonly IdeaAuto Gone = Auto("Пробои 5м", Noon.AddDays(-1));

    private readonly Guid _account = Guid.NewGuid();
    private IIdeaTradeRepository _trades = null!;
    private IPaperIdeaTradeRepository _paper = null!;
    private IIdeaAutoRepository _autos = null!;
    private IIdeaRepository _ideas = null!;
    private IMarketHistoryClient _history = null!;
    private GetIdeaJournalQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _trades = Substitute.For<IIdeaTradeRepository>();
        _history = Substitute.For<IMarketHistoryClient>();
        _paper = Substitute.For<IPaperIdeaTradeRepository>();
        _paper.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _autos = Substitute.For<IIdeaAutoRepository>();
        _autos.GetEveryAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IdeaAuto> { [Impulse.Id] = Impulse, [Gone.Id] = Gone });
        _ideas = Substitute.For<IIdeaRepository>();
        _ideas.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ExchangeAccount(_account, "Основной", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Noon)]);
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };
        _handler = new GetIdeaJournalQueryHandler(_trades, _paper, _autos, _ideas, accounts, _history, backtests, new FakeTimeProvider(Noon));
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
        Prices(("ETHUSDT", 23.90m));

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        IdeaJournalTrade item = result.Journal!.Open.Single();
        decimal expected = 11.233m - 11.0356m - 0.0061m - (11.233m * 0.00055m);
        (item.LastPrice, item.CurrentResult, item.CurrentResultR).ShouldBe((23.90m, expected, expected / 0.2m));
    }

    // One request for the prices of a category, however many trades are open on a pair; a trade that waits for its entry has no result and asks for nothing.
    [Test]
    public async Task Last_prices_are_asked_once_and_not_for_a_trade_that_waits()
    {
        Trades([Opened(Noon.AddHours(-1), Eth), Opened(Noon.AddHours(-2), Eth), Waiting(Noon.AddHours(-3), instrument: new Instrument(MarketCategory.Spot, "BTCUSDT"))]);
        Prices(("ETHUSDT", 23.90m));

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        await _history.Received(1).GetLastPricesAsync(MarketCategory.Linear, Arg.Any<CancellationToken>());
        await _history.DidNotReceive().GetLastPricesAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
        result.Journal!.Open.Select(item => item.CurrentResult is not null).ShouldBe([true, true, false]);
    }

    // 100 open paper trades on prod took the journal 23 s, a request of the price after another: the prices of a category come in one request.
    [Test]
    public async Task Last_prices_are_asked_once_for_a_category_however_many_pairs_are_open()
    {
        Trades([Opened(Noon.AddHours(-1), Eth), Opened(Noon.AddHours(-2), Btc)]);
        _history.GetLastPricesAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(new Dictionary<string, decimal> { ["ETHUSDT"] = 23.90m, ["BTCUSDT"] = 24m });

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        await _history.Received(1).GetLastPricesAsync(MarketCategory.Linear, Arg.Any<CancellationToken>());
        await _history.DidNotReceiveWithAnyArgs().GetLastPriceAsync(default!, default);
        result.Journal!.Open.Select(item => item.LastPrice).ShouldBe([23.90m, 24m]);
    }

    // Scenario "Bybit не ответил": the journal answers without an error; the trades of the category that got no price have no current result,
    // nor does a pair the tickers do not name.
    [Test]
    public async Task Exchange_that_gives_no_price_leaves_the_trade_without_a_current_result()
    {
        var sol = new Instrument(MarketCategory.Spot, "SOLUSDT");
        Trades([Opened(Noon.AddHours(-1), Eth), Opened(Noon.AddHours(-2), Btc), Opened(Noon.AddHours(-3), sol)]);
        _history.GetLastPricesAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns<IReadOnlyDictionary<string, decimal>>(_ => throw new ExchangeUnavailableException("down"));
        Prices(("BTCUSDT", 23.90m));

        GetIdeaJournalResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Journal!.Open.Select(item => item.CurrentResult is not null).ShouldBe([false, true, false]);
        (result.Journal.Open[0].LastPrice, result.Journal.Open[0].CurrentResultR).ShouldBe((null, null));
    }

    private void Prices(params (string Symbol, decimal Price)[] linear)
    {
        _history.GetLastPricesAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(linear.ToDictionary(item => item.Symbol, item => item.Price));
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

    // Requirements of the journal of the autos: a trade carries who took it, the paper ones stand with the real ones, and the filters and the totals tell them apart.
    [Test]
    public async Task Paper_trades_stand_among_the_real_ones_with_who_took_them_and_the_name_of_the_auto_a_deleted_one_too()
    {
        IdeaTrade manual = Waiting(Noon.AddHours(-3));
        IdeaTrade auto = Waiting(Noon.AddHours(-2)) with { AutoId = Impulse.Id };
        IdeaTrade paperOpen = Waiting(Noon.AddHours(-1)) with { AutoId = Gone.Id };
        IdeaTrade paperClosed = Closed(Noon.AddMinutes(-30)) with { AutoId = Gone.Id };
        IdeaTrade realClosed = Closed(Noon.AddMinutes(-10));
        Trades([manual, auto, realClosed]);
        Paper([paperOpen, paperClosed]);

        IdeaJournal journal = (await _handler.Handle(Everything, CancellationToken.None)).Journal!;

        journal.Open.Select(item => (item.Trade.Id, item.Origin, item.AutoName)).ShouldBe([(paperOpen.Id, IdeaTradeOrigin.Paper, "Пробои 5м"), (auto.Id, IdeaTradeOrigin.Auto, "Импульс лонг"), (manual.Id, IdeaTradeOrigin.Manual, null)]);
        journal.Closed.Items.Select(item => (item.Trade.Id, item.Origin)).ShouldBe([(realClosed.Id, IdeaTradeOrigin.Manual), (paperClosed.Id, IdeaTradeOrigin.Paper)]);
    }

    // Scenario "Учебные отдельно": two real trades and three paper ones, two at the target and one at the stop.
    [Test]
    public async Task Totals_of_the_real_trades_leave_out_the_paper_ones_which_have_their_own()
    {
        Trades([Closed(Noon.AddMinutes(-1)), Closed(Noon.AddMinutes(-2))]);
        Paper([Closed(Noon.AddMinutes(-3)), Closed(Noon.AddMinutes(-4)), Stopped(Noon.AddMinutes(-5))]);

        IdeaJournal journal = (await _handler.Handle(Everything, CancellationToken.None)).Journal!;

        (journal.Totals.Targets, journal.Totals.Stops).ShouldBe((2, 0));
        (journal.PaperTotals.Targets, journal.PaperTotals.Stops, journal.PaperTotals.Manual).ShouldBe((2, 1, 0));
    }

    // Scenario "Только учебные": the real totals are zeros.
    [TestCase("paper", 0, 3)]
    [TestCase("manual", 1, 0)]
    [TestCase("auto", 2, 0)]
    public async Task Who_took_it_filters_the_trades_and_both_kinds_of_totals(string origin, int real, int paper)
    {
        Trades([Closed(Noon.AddMinutes(-1)), Closed(Noon.AddMinutes(-2)) with { AutoId = Impulse.Id }, Closed(Noon.AddMinutes(-3)) with { AutoId = Impulse.Id }]);
        Paper([Closed(Noon.AddMinutes(-4)) with { AutoId = Gone.Id }, Closed(Noon.AddMinutes(-5)) with { AutoId = Gone.Id }, Closed(Noon.AddMinutes(-6)) with { AutoId = Gone.Id }]);

        IdeaJournal journal = (await _handler.Handle(Everything with { Origin = origin }, CancellationToken.None)).Journal!;

        journal.Totals.Targets.ShouldBe(real);
        journal.PaperTotals.Targets.ShouldBe(paper);
        journal.Closed.Total.ShouldBe(real + paper);
    }

    // Scenario "Один автомат": its real and its paper trades, the open ones and the closed ones.
    [Test]
    public async Task One_auto_leaves_its_real_and_paper_trades_only()
    {
        IdeaTrade mine = Closed(Noon.AddMinutes(-1)) with { AutoId = Impulse.Id };
        IdeaTrade minePaper = Waiting(Noon.AddMinutes(-2)) with { AutoId = Impulse.Id };
        Trades([mine, Closed(Noon.AddMinutes(-3)), Closed(Noon.AddMinutes(-4)) with { AutoId = Gone.Id }]);
        Paper([minePaper, Closed(Noon.AddMinutes(-5)) with { AutoId = Gone.Id }]);

        IdeaJournal journal = (await _handler.Handle(Everything with { AutoId = Impulse.Id }, CancellationToken.None)).Journal!;

        journal.Closed.Items.Select(item => item.Trade.Id).ShouldBe([mine.Id]);
        journal.Open.Select(item => item.Trade.Id).ShouldBe([minePaper.Id]);
        (journal.Totals.Targets, journal.PaperTotals.Targets).ShouldBe((1, 0));
    }

    [Test]
    public async Task Unknown_origin_and_auto_are_errors_named_by_the_parameter()
    {
        GetIdeaJournalResult result = await _handler.Handle(Everything with { Origin = "robot", AutoId = Guid.NewGuid() }, CancellationToken.None);

        result.Journal.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["origin", "autoId"], ignoreOrder: true);
    }

    // Decision 14: an open paper trade is worth what a real one is, at the last price of Bybit.
    [Test]
    public async Task Open_paper_trade_gets_its_current_result_at_the_last_price()
    {
        IdeaTrade paperOpen = Opened(Noon.AddHours(-1), Eth) with { AutoId = Gone.Id };
        Paper([paperOpen]);
        Prices(("ETHUSDT", 24m));

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Open.Single();

        (view.Origin, view.LastPrice).ShouldBe((IdeaTradeOrigin.Paper, 24m));
        view.CurrentResult.ShouldNotBeNull();
    }

    // Review P3: a deleted auto does not hold its account, so the account of its paper trades may be gone; the trade stays, with no account name.
    [Test]
    public async Task Paper_trade_of_an_account_that_is_gone_has_no_account_name()
    {
        Paper([Closed(Noon.AddHours(-1)) with { AccountId = Guid.NewGuid() }]);

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Closed.Items.Single();

        view.AccountName.ShouldBe("—");
    }

    // Requirement "Конец идеи у сделки в работе", scenario "Активная идея": an idea of 15m that lives 16 candles and entered at 10:00 ends at 14:00.
    [Test]
    public async Task Open_trade_of_an_active_idea_has_the_end_of_its_lifetime()
    {
        Idea idea = Active(Noon.AddHours(-2), 15, 16);
        Trades([Of(idea, Noon.AddHours(-2)).EntryGrown(new LegTotals(0.47m, 11.0356m, 0.0061m), Noon.AddHours(-2).AddMinutes(1))]);
        Ideas(idea);

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Open.Single();

        view.IdeaEndsAt.ShouldBe(new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero));
    }

    // Scenario "Идея ждёт входа": a pullback that waits for its entry, and so does its idea.
    [Test]
    public async Task Trade_of_an_idea_that_waits_for_its_entry_has_no_end_of_the_idea()
    {
        Idea idea = IdeaGenerator.Generate(Noon.AddMinutes(-30), type: IdeaType.Pullback);
        Trades([Of(idea, Noon.AddMinutes(-30))]);
        Ideas(idea);

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Open.Single();

        view.IdeaEndsAt.ShouldBeNull();
    }

    // Scenario "Идея закрылась раньше сделки": the idea expired and is still stored, and the trade has its outcome.
    [Test]
    public async Task Trade_whose_idea_closed_has_the_outcome_of_the_idea_and_no_end_of_it()
    {
        Idea idea = IdeaGenerator.Generate(Noon.AddHours(-8), IdeaStatus.Expired, statusAt: Noon.AddHours(-2));
        IdeaTrade opened = Of(idea, Noon.AddHours(-8)).EntryGrown(new LegTotals(0.47m, 11.0356m, 0.0061m), Noon.AddHours(-8).AddMinutes(1));
        Trades([opened with { IdeaOutcome = new TradedIdeaOutcome(IdeaStatus.Expired, Noon.AddHours(-2), 0.4m) }]);
        Ideas(idea);

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Open.Single();

        view.IdeaEndsAt.ShouldBeNull();
        view.Trade.IdeaOutcome.ShouldNotBeNull();
    }

    // An idea that is no longer stored gives no end.
    [Test]
    public async Task Trade_of_an_idea_that_is_no_longer_stored_has_no_end_of_the_idea()
    {
        Trades([Of(Active(Noon.AddHours(-2), 15, 16), Noon.AddHours(-2))]);

        IdeaJournalTrade view = (await _handler.Handle(Everything, CancellationToken.None)).Journal!.Open.Single();

        view.IdeaEndsAt.ShouldBeNull();
    }

    // Scenario "Учебная сделка": a paper auto took an active idea of 5m that lives 48 candles and entered at 09:30; the ideas of the real and the
    // paper trades come in one read.
    [Test]
    public async Task Open_paper_trade_has_the_end_of_its_idea_read_with_the_ideas_of_the_real_ones()
    {
        Idea paperIdea = Active(Noon.AddHours(-2.5), 5, 48);
        Idea realIdea = Active(Noon.AddHours(-2), 15, 16);
        IdeaTrade paperTrade = Of(paperIdea, Noon.AddHours(-2.5));
        Trades([Of(realIdea, Noon.AddHours(-2))]);
        Paper([paperTrade]);
        Ideas(paperIdea, realIdea);

        IdeaJournal journal = (await _handler.Handle(Everything, CancellationToken.None)).Journal!;

        journal.Open.Single(item => item.Trade.Id == paperTrade.Id).IdeaEndsAt.ShouldBe(new DateTimeOffset(2026, 11, 6, 13, 30, 0, TimeSpan.Zero));
        await _ideas.Received(1).GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _ideas.Received(1).GetManyAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Order().SequenceEqual(new[] { paperIdea.Id, realIdea.Id }.Order())), Arg.Any<CancellationToken>());
    }

    // A closed trade has no end of its idea, and its idea is not read.
    [Test]
    public async Task Closed_trade_has_no_end_of_its_idea_and_its_idea_is_not_read()
    {
        Idea openIdea = Active(Noon.AddHours(-2), 15, 16);
        IdeaTrade closed = Closed(Noon.AddMinutes(-10));
        Trades([Of(openIdea, Noon.AddHours(-2)), closed]);
        Ideas(openIdea, Active(Noon.AddHours(-3), 15, 16) with { Id = closed.IdeaId });

        IdeaJournal journal = (await _handler.Handle(Everything, CancellationToken.None)).Journal!;

        journal.Closed.Items.Single().IdeaEndsAt.ShouldBeNull();
        await _ideas.Received(1).GetManyAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { openIdea.Id })), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Without_open_trades_no_idea_is_read()
    {
        Trades([Closed(Noon.AddMinutes(-10))]);

        await _handler.Handle(Everything, CancellationToken.None);

        await _ideas.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }

    private void Ideas(params Idea[] ideas)
    {
        _ideas.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ideas);
    }

    // An active idea that entered when it fired, on candles of the minutes given, living the candles given.
    private static Idea Active(DateTimeOffset enteredAt, int candleMinutes, int lifeCandles)
    {
        Idea idea = IdeaGenerator.Generate(enteredAt, IdeaStatus.Active);

        return idea with { Track = idea.Track with { CandleMinutes = candleMinutes, LifeCandles = lifeCandles } };
    }

    private IdeaTrade Of(Idea idea, DateTimeOffset createdAt)
    {
        return IdeaTrade.Create(Guid.NewGuid(), idea, _account, 100m, Size(), createdAt);
    }

    private void Paper(IEnumerable<IdeaTrade> trades)
    {
        _paper.GetAllAsync(Arg.Any<CancellationToken>()).Returns([.. trades.Select(trade => trade with { AutoId = trade.AutoId ?? Gone.Id }).Select(trade => new PaperIdeaTrade(trade, trade.AutoId!.Value, trade.StatusAt))]);
    }

    private IdeaTrade Stopped(DateTimeOffset closedAt)
    {
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(closedAt.AddHours(-3)), _account, 100m, Size(), closedAt.AddHours(-3))
            .EntryGrown(new LegTotals(1m, 100m, 0m), closedAt.AddHours(-2))
            .ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(1m, 98m, 0m), closedAt);

        return trade with { AutoId = Gone.Id };
    }

    private static IdeaAuto Auto(string name, DateTimeOffset? deletedAt)
    {
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FifteenMinutes], null, null, null, 1.2m, 0);

        return IdeaAuto.Create(Guid.NewGuid(), name, Guid.NewGuid(), 300m, conditions, 0) with { DeletedAt = deletedAt };
    }

    private static IdeaTradeSize Size()
    {
        return new IdeaTradeSize(1m, 100m, 2m, 4m, 2m);
    }
}