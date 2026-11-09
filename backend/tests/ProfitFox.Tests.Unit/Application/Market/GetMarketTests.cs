using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Market.Queries.GetMarket;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.SavedFilters;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Market;

/// <summary>Requirements "Чтение снимка рынка" and "Фильтр, поиск и сортировка": the page of the snapshot, its filters, its order and its counts.</summary>
public class GetMarketTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    // The day ceiling of 30 days before noon: the days of history the scanner keeps.
    private static readonly DateTimeOffset HistoryStart = DateTimeOffset.Parse("2024-02-10T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly PairMetrics None = MarketSnapshotRowGenerator.NoMetrics;
    private static readonly GetMarketQuery Everything = new GetMarketQuery(null, null, null, null, null, null, null, null, null);

    private IMarketSnapshotRepository _snapshots = null!;
    private IScannerStateRepository _state = null!;
    private ISavedFilterRepository _filters = null!;
    private IIdeaReplayRepository _cursors = null!;
    private GetMarketQueryHandler _handler = null!;
    private IReadOnlyList<MarketSnapshotRow> _rows = [];

    [SetUp]
    public void SetUp()
    {
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _state = Substitute.For<IScannerStateRepository>();
        _filters = Substitute.For<ISavedFilterRepository>();
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(Noon, 1104, 1104, null));
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _snapshots.GetSetupsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IReadOnlyList<PairSetup>>());
        _cursors = Substitute.For<IIdeaReplayRepository>();
        _cursors.ProgressAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new IdeaReplayProgress(0, 0, null, IdeasEngineVersion.Current));
        _handler = new GetMarketQueryHandler(_snapshots, _state, _filters, _cursors, new ScannerSettings { HistoryDays = 30 }, new FakeTimeProvider(Noon));
    }

    // Scenario "Ход прогона в ответе" (decision 4 of speed-up-ideas-replay): the state carries the progress of the replay of the version of the rules
    // this build runs.
    [Test]
    public async Task State_carries_the_progress_of_the_replay_of_the_current_version()
    {
        var progress = new IdeaReplayProgress(507, 1211, DateTimeOffset.Parse("2026-08-15T00:00:00Z", CultureInfo.InvariantCulture), IdeasEngineVersion.Current);
        _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, Arg.Any<CancellationToken>()).Returns(progress);

        GetMarketResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.State.Replay.ShouldBe(progress);
    }

    // Scenario "Снимок есть": the first rows by the change in 1 hour, the count of all the pairs and the state.
    [Test]
    public async Task Nothing_asked_gives_the_first_50_pairs_by_the_change_in_1_hour_highest_first()
    {
        Rows([.. Enumerable.Range(0, 120).Select(number => Row($"P{number:000}USDT", None with { Change1h = number }))]);

        GetMarketResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Page!.Items.Select(row => row.Instrument.Symbol).ShouldBe([.. Enumerable.Range(0, 50).Select(number => $"P{119 - number:000}USDT")]);
        (result.Page.Number, result.Page.Size, result.Page.Total, result.Counts!.Pairs).ShouldBe((1, 50, 120, 120));
        result.State.SnapshotAt.ShouldBe(Noon);
    }

    // Scenario "Вторая страница рынка".
    [Test]
    public async Task The_second_page_of_100_holds_pairs_101_to_120()
    {
        Rows([.. Enumerable.Range(0, 120).Select(number => Row($"P{number:000}USDT", None with { Change1h = number }))]);

        GetMarketResult result = await _handler.Handle(Everything with { Page = 2, PageSize = 100 }, CancellationToken.None);

        (result.Page!.Items.Count, result.Page.Number, result.Page.Size).ShouldBe((20, 2, 100));
    }

    [Test]
    public async Task A_page_past_the_end_gives_the_last_page()
    {
        Rows([.. Enumerable.Range(0, 120).Select(number => Row($"P{number:000}USDT"))]);

        GetMarketResult result = await _handler.Handle(Everything with { Page = 9 }, CancellationToken.None);

        (result.Page!.Items.Count, result.Page.Number).ShouldBe((20, 3));
    }

    // Scenario "Сортировка по всем страницам рынка".
    [Test]
    public async Task A_column_orders_all_the_pairs_before_the_page_is_cut()
    {
        Rows([.. Enumerable.Range(0, 120).Select(number => Row($"P{number:000}USDT", None with { Change1h = number, Turnover24h = number == 0 ? 1000000m : 1m }))]);

        GetMarketResult result = await _handler.Handle(Everything with { Sort = "turnover24h", Direction = "desc" }, CancellationToken.None);

        result.Page!.Items[0].Instrument.Symbol.ShouldBe("P000USDT");
    }

    // Scenario "Сканер ни разу не работал".
    [Test]
    public async Task Without_a_snapshot_the_page_is_empty_and_the_state_has_no_time()
    {
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(null, 0, 0, null));

        GetMarketResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Page!.Items.ShouldBeEmpty();
        (result.Page.Number, result.Page.Total, result.Counts!.Pairs).ShouldBe((1, 0, 0));
        result.State.SnapshotAt.ShouldBeNull();
    }

    [Test]
    public async Task A_pause_of_rest_shows_only_while_it_lasts()
    {
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(Noon, 1, 1, Noon.AddMinutes(5)));
        (await _handler.Handle(Everything, CancellationToken.None)).State.RestPausedUntil.ShouldBe(Noon.AddMinutes(5));

        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(Noon, 1, 1, Noon));
        (await _handler.Handle(Everything, CancellationToken.None)).State.RestPausedUntil.ShouldBeNull();
    }

    [Test]
    public async Task The_category_cuts_the_pairs_but_not_the_counts_of_the_categories()
    {
        Rows([Row("AAAUSDT"), Row("BBBUSDT"), Row("CCCUSDT", category: MarketCategory.Spot)]);

        GetMarketResult result = await _handler.Handle(Everything with { Category = "spot" }, CancellationToken.None);

        Symbols(result).ShouldBe(["CCCUSDT"]);
        result.Counts!.Categories.ShouldBe(new Dictionary<MarketCategory, int> { [MarketCategory.Linear] = 2, [MarketCategory.Spot] = 1 }, ignoreOrder: true);
        result.Counts.Pairs.ShouldBe(3);
    }

    // Scenario "Поиск на второй странице рынка": the search is a part of the symbol, without regard to case.
    [Test]
    public async Task The_search_keeps_the_pairs_whose_symbol_has_that_part()
    {
        Rows([Row("BTCUSDT"), Row("WBTCUSDT"), Row("ETHUSDT")]);

        GetMarketResult result = await _handler.Handle(Everything with { Search = "btc", Sort = "symbol", Direction = "asc" }, CancellationToken.None);

        Symbols(result).ShouldBe(["BTCUSDT", "WBTCUSDT"]);
        result.Page!.Total.ShouldBe(2);
    }

    [Test]
    public async Task The_setup_keeps_the_pairs_that_have_it_on_the_interval_15_minutes_by_default()
    {
        Rows([Row("AAAUSDT"), Row("BBBUSDT"), Row("CCCUSDT")]);
        Setups(("AAAUSDT", Interval.FifteenMinutes, IdeaSetup.Impulse), ("BBBUSDT", Interval.OneHour, IdeaSetup.Impulse), ("CCCUSDT", Interval.FifteenMinutes, IdeaSetup.Drop));

        Symbols(await _handler.Handle(Everything with { Setup = "impulse" }, CancellationToken.None)).ShouldBe(["AAAUSDT"]);
        Symbols(await _handler.Handle(Everything with { Setup = "impulse", Interval = "60" }, CancellationToken.None)).ShouldBe(["BBBUSDT"]);
    }

    // Scenario "Чипы считают остальные фильтры".
    [Test]
    public async Task The_chips_count_the_pairs_left_by_the_category_the_search_and_the_filter_but_not_by_the_setup()
    {
        Rows(
        [
            Row("AAAUSDT"),
            Row("BBBUSDT"),
            Row("CCCUSDT"),
            Row("DDDUSDC"),
            Row("EEEUSDT", category: MarketCategory.Spot)
        ]);
        Setups(
            ("AAAUSDT", Interval.FifteenMinutes, IdeaSetup.Impulse),
            ("BBBUSDT", Interval.FifteenMinutes, IdeaSetup.Impulse),
            ("BBBUSDT", Interval.FifteenMinutes, IdeaSetup.HighVolatility),
            ("CCCUSDT", Interval.OneHour, IdeaSetup.Impulse),
            ("DDDUSDC", Interval.FifteenMinutes, IdeaSetup.Impulse),
            ("EEEUSDT", Interval.FifteenMinutes, IdeaSetup.Impulse));

        GetMarketResult result = await _handler.Handle(Everything with { Category = "linear", Search = "USDT", Setup = "impulse" }, CancellationToken.None);

        Symbols(result).ShouldBe(["AAAUSDT", "BBBUSDT"], ignoreOrder: true);
        result.Counts!.Filtered.ShouldBe(3);
        result.Counts.Setups[IdeaSetup.Impulse].ShouldBe(2);
        result.Counts.Setups[IdeaSetup.HighVolatility].ShouldBe(1);
        result.Counts.Setups[IdeaSetup.Drop].ShouldBe(0);
        result.Counts.Categories.Values.Sum().ShouldBe(5);
        result.Counts.Pairs.ShouldBe(5);
    }

    [Test]
    public async Task A_saved_filter_keeps_the_pairs_that_meet_it_and_skips_its_conditions_on_ideas()
    {
        Rows([Row("AAAUSDT", None with { Turnover24h = 60000000m }), Row("BBBUSDT", None with { Turnover24h = 1m })]);
        SavedFilter filter = Filter(new FilterCondition(FilterField.Turnover24h, FilterOperator.Greater, 50000000m), new FilterCondition(FilterField.Expectancy, FilterOperator.Greater, 5m));

        GetMarketResult result = await _handler.Handle(Everything with { SavedFilter = filter.Id }, CancellationToken.None);

        Symbols(result).ShouldBe(["AAAUSDT"]);
        (result.Page!.Total, result.Counts!.Filtered, result.Counts.Pairs).ShouldBe((1, 1, 2));
    }

    [Test]
    public async Task Wrong_codes_are_errors_keyed_by_the_parameter_name()
    {
        GetMarketResult result = await _handler.Handle(
            Everything with { Category = "x", Setup = "x", Interval = "7", Sort = "volume", Direction = "up", Page = 0, PageSize = 75, SavedFilter = Guid.NewGuid() },
            CancellationToken.None);

        result.Page.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["category", "setup", "interval", "sort", "direction", "page", "pageSize", "savedFilter"], ignoreOrder: true);
    }

    [Test]
    public async Task The_setups_of_the_pairs_come_with_the_page()
    {
        Rows([Row("AAAUSDT")]);
        Setups(("AAAUSDT", Interval.FifteenMinutes, IdeaSetup.Impulse));

        GetMarketResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Setups[new Instrument(MarketCategory.Linear, "AAAUSDT")].ShouldBe([new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)]);
    }

    private void Rows(IReadOnlyList<MarketSnapshotRow> rows)
    {
        _rows = rows;
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(rows);
    }

    private void Setups(params (string Symbol, Interval Interval, IdeaSetup Setup)[] setups)
    {
        _snapshots.GetSetupsAsync(Arg.Any<CancellationToken>()).Returns(
            setups.GroupBy(setup => setup.Symbol)
                .ToDictionary(
                    group => _rows.Single(row => row.Instrument.Symbol == group.Key).Instrument,
                    group => (IReadOnlyList<PairSetup>)[.. group.Select(setup => new PairSetup(setup.Interval, setup.Setup))]));
    }

    private SavedFilter Filter(params FilterCondition[] conditions)
    {
        var filter = new SavedFilter(Guid.NewGuid(), "Filter", SavedFilterPlace.Both, conditions, Noon);
        _filters.GetAsync(filter.Id, Arg.Any<CancellationToken>()).Returns(filter);

        return filter;
    }

    private static MarketSnapshotRow Row(string symbol, PairMetrics? metrics = null, MarketCategory category = MarketCategory.Linear)
    {
        return MarketSnapshotRowGenerator.Generate(symbol, category, metrics);
    }

    private static string[] Symbols(GetMarketResult result)
    {
        return [.. result.Page!.Items.Select(row => row.Instrument.Symbol)];
    }
}