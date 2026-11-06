using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ideas.Queries.GetIdeas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.SavedFilters;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>Requirements "Чтение идей" and "Сортировка списка идей": the page of ideas, its filters and its order.</summary>
public class GetIdeasTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly GetIdeasQuery Everything = new GetIdeasQuery(null, null, null, null, null, false, null, null, null, null, null);

    private IIdeaRepository _ideas = null!;
    private IIdeaBucketRepository _buckets = null!;
    private IMarketSnapshotRepository _snapshots = null!;
    private ISavedFilterRepository _filters = null!;
    private IScannerPairRepository _pairs = null!;
    private GetIdeasQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _ideas = Substitute.For<IIdeaRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _filters = Substitute.For<ISavedFilterRepository>();
        _pairs = Substitute.For<IScannerPairRepository>();
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([]);
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _snapshots.GetSetupsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IReadOnlyList<PairSetup>>());
        var backtests = new BacktestSettings
        {
            DefaultFees = new Dictionary<string, FeeRateSettings>
            {
                ["spot"] = new FeeRateSettings { Limit = 0.1m, Market = 0.1m },
                ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m }
            }
        };
        _handler = new GetIdeasQueryHandler(_ideas, _buckets, _snapshots, _filters, _pairs, new IdeasEngineVersion(IdeasEngineVersion.Current), backtests, new FakeTimeProvider(Noon));
    }

    // Scenario "Страница идей": the ideas come newest first, as they have no bucket and so equal expectancy.
    [Test]
    public async Task The_third_page_of_50_holds_the_last_20_of_120_ideas()
    {
        Ideas(120);

        GetIdeasResult result = await _handler.Handle(Everything with { Page = 3 }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([.. Enumerable.Range(100, 20).Select(Id)]);
        (result.Page.Number, result.Page.Size, result.Page.Total).ShouldBe((3, 50, 120));
    }

    [Test]
    public async Task Nothing_asked_gives_the_first_page_of_50()
    {
        Ideas(120);

        GetIdeasResult result = await _handler.Handle(Everything, CancellationToken.None);

        (result.Page!.Items.Count, result.Page.Number, result.Page.Size).ShouldBe((50, 1, 50));
    }

    // Scenario "Страница за концом списка".
    [Test]
    public async Task A_page_past_the_end_gives_the_last_page()
    {
        Ideas(30);

        GetIdeasResult result = await _handler.Handle(Everything with { Page = 2 }, CancellationToken.None);

        (result.Page!.Items.Count, result.Page.Number).ShouldBe((30, 1));
    }

    [Test]
    public async Task The_size_asked_is_the_size_of_the_page()
    {
        Ideas(120);

        GetIdeasResult result = await _handler.Handle(Everything with { PageSize = 100, Page = 2 }, CancellationToken.None);

        (result.Page!.Items.Count, result.Page.Size).ShouldBe((20, 100));
    }

    [Test]
    public async Task The_ideas_go_by_the_expectancy_highest_first_when_no_column_is_named()
    {
        Idea impulse = Idea(1, IdeaSetup.Impulse);
        Idea drop = Idea(2, IdeaSetup.Drop);
        Idea early = Idea(3, IdeaSetup.EarlyDrop);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([impulse, drop, early]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([Bucket(impulse, 0.1m), Bucket(drop, 0.5m)]);

        GetIdeasResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([drop.Id, impulse.Id, early.Id]);
    }

    [Test]
    public async Task The_column_and_direction_order_all_the_ideas_not_only_the_page()
    {
        Ideas(120);
        Idea best = Idea(500) with { Track = Idea(500).Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99m, 150m) } };
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, 120).Select(number => Idea(number)), best]);

        GetIdeasResult result = await _handler.Handle(Everything with { Sort = "riskReward", Direction = "desc" }, CancellationToken.None);

        result.Page!.Items[0].Idea.Id.ShouldBe(best.Id);
    }

    [Test]
    public async Task The_filters_cut_the_list_and_the_total_counts_only_what_is_left()
    {
        Idea long1 = Idea(1);
        Idea short1 = Idea(2, side: IdeaSide.Short);
        Idea spot = Idea(3) with { Instrument = new Instrument(MarketCategory.Spot, "ETHUSDT") };
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([long1, short1, spot]);

        GetIdeasResult result = await _handler.Handle(Everything with { Side = "long", Category = "linear" }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([long1.Id]);
        result.Page.Total.ShouldBe(1);
    }

    // Scenario "Только активные".
    [Test]
    public async Task Only_active_keeps_the_ideas_awaiting_entry_and_the_active_ones()
    {
        Idea awaiting = Idea(1);
        Idea active = Idea(2, status: IdeaStatus.Active);
        Idea target = Idea(3, status: IdeaStatus.Target);
        Idea stop = Idea(4, status: IdeaStatus.Stop);
        Idea expired = Idea(5, status: IdeaStatus.Expired);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([awaiting, active, target, stop, expired]);

        GetIdeasResult result = await _handler.Handle(Everything with { ActiveOnly = true }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([awaiting.Id, active.Id]);
        result.Page.Total.ShouldBe(2);
    }

    // Scenario "Скрыть акции"; an idea of a pair the list has not told about yet is crypto and stays.
    [Test]
    public async Task Hide_tradfi_drops_the_ideas_of_non_crypto_pairs()
    {
        Idea btc = Idea(1) with { Instrument = new Instrument(MarketCategory.Linear, "BTCUSDT") };
        Idea apple = Idea(2) with { Instrument = new Instrument(MarketCategory.Linear, "AAPLUSDT") };
        Idea etf = Idea(3) with { Instrument = new Instrument(MarketCategory.Linear, "ARKKUSDT") };
        Idea gold = Idea(4) with { Instrument = new Instrument(MarketCategory.Linear, "XAUUSDT") };
        Idea euro = Idea(5) with { Instrument = new Instrument(MarketCategory.Linear, "EURUSDUSDT") };
        Idea tesla = Idea(6) with { Instrument = new Instrument(MarketCategory.Spot, "TSLAXUSDT") };
        Idea unknown = Idea(7) with { Instrument = new Instrument(MarketCategory.Linear, "NEWUSDT") };
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([btc, apple, etf, gold, euro, tesla, unknown]);
        _pairs.GetTradFiAsync(Arg.Any<CancellationToken>()).Returns(
            [apple.Instrument, etf.Instrument, gold.Instrument, euro.Instrument, tesla.Instrument, new Instrument(MarketCategory.Spot, "BTCUSDT")]);

        GetIdeasResult result = await _handler.Handle(Everything with { HideTradFi = true }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([btc.Id, unknown.Id]);
        result.Page.Total.ShouldBe(2);
    }

    // Decision 5: the pairs are read only for the flag.
    [Test]
    public async Task Without_hide_tradfi_the_pairs_are_not_read()
    {
        Ideas(3);

        GetIdeasResult result = await _handler.Handle(Everything, CancellationToken.None);

        result.Page!.Total.ShouldBe(3);
        await _pairs.DidNotReceive().GetTradFiAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Only_non_negative_expectancy_drops_the_ideas_without_a_bucket_and_the_negative_ones()
    {
        Idea good = Idea(1, IdeaSetup.Impulse);
        Idea bad = Idea(2, IdeaSetup.Drop);
        Idea none = Idea(3, IdeaSetup.EarlyDrop);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([good, bad, none]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([Bucket(good, 0m), Bucket(bad, -0.1m)]);

        GetIdeasResult result = await _handler.Handle(Everything with { NonNegativeExpectancy = true }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([good.Id]);
    }

    [Test]
    public async Task Wrong_codes_are_errors_keyed_by_the_parameter_name()
    {
        GetIdeasResult result = await _handler.Handle(
            Everything with { Setup = "x", Interval = "7", Type = "x", Side = "x", Category = "x", Sort = "move", Direction = "up", Page = 0, PageSize = 75 },
            CancellationToken.None);

        result.Page.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["setup", "interval", "type", "side", "category", "sort", "direction", "page", "pageSize"], ignoreOrder: true);
    }

    [Test]
    public async Task A_saved_filter_that_does_not_exist_is_an_error_of_the_saved_filter()
    {
        _filters.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SavedFilter?)null);

        GetIdeasResult result = await _handler.Handle(Everything with { SavedFilter = Guid.NewGuid() }, CancellationToken.None);

        result.Errors.Keys.ShouldBe(["savedFilter"]);
    }

    [Test]
    public async Task A_filter_with_conditions_on_the_idea_only_does_not_read_the_snapshot()
    {
        Idea good = Idea(1, IdeaSetup.Impulse);
        Idea bad = Idea(2, IdeaSetup.Drop);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([good, bad]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([Bucket(good, 0.3m), Bucket(bad, 0.1m)]);
        SavedFilter filter = Filter(new FilterCondition(FilterField.Expectancy, FilterOperator.Greater, 0.2m));

        GetIdeasResult result = await _handler.Handle(Everything with { SavedFilter = filter.Id }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([good.Id]);
        result.Page.Total.ShouldBe(1);
        await _snapshots.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
        await _snapshots.DidNotReceive().GetSetupsAsync(Arg.Any<CancellationToken>());
    }

    // Scenario "Сохранённый фильтр в запросе" and "Идея пары без строки снимка".
    [Test]
    public async Task A_filter_with_a_market_condition_keeps_the_ideas_whose_pair_meets_it_in_the_snapshot()
    {
        Idea rich = Idea(1) with { Instrument = new Instrument(MarketCategory.Linear, "SOLUSDT") };
        Idea poor = Idea(2) with { Instrument = new Instrument(MarketCategory.Linear, "ARMUSDT") };
        Idea absent = Idea(3) with { Instrument = new Instrument(MarketCategory.Linear, "WIFUSDT") };
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([rich, poor, absent]);
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            MarketSnapshotRowGenerator.Generate("SOLUSDT", metrics: MarketSnapshotRowGenerator.NoMetrics with { Turnover24h = 60000000m }),
            MarketSnapshotRowGenerator.Generate("ARMUSDT", metrics: MarketSnapshotRowGenerator.NoMetrics with { Turnover24h = 1m })
        ]);
        SavedFilter filter = Filter(new FilterCondition(FilterField.Turnover24h, FilterOperator.Greater, 50000000m));

        GetIdeasResult result = await _handler.Handle(Everything with { SavedFilter = filter.Id }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([rich.Id]);
        result.Page.Total.ShouldBe(1);
    }

    [Test]
    public async Task A_filter_with_a_setup_condition_reads_the_setups_of_the_pairs()
    {
        Idea hot = Idea(1);
        Idea cold = Idea(2) with { Instrument = new Instrument(MarketCategory.Linear, "ARMUSDT") };
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([hot, cold]);
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns([MarketSnapshotRowGenerator.Generate("WIFUSDT"), MarketSnapshotRowGenerator.Generate("ARMUSDT")]);
        _snapshots.GetSetupsAsync(Arg.Any<CancellationToken>()).Returns(
            new Dictionary<Instrument, IReadOnlyList<PairSetup>> { [hot.Instrument] = [new PairSetup(Interval.OneHour, IdeaSetup.Drop)] });
        SavedFilter filter = Filter(new FilterCondition(FilterField.Setup, FilterOperator.Equal, Choice: "drop"));

        GetIdeasResult result = await _handler.Handle(Everything with { SavedFilter = filter.Id }, CancellationToken.None);

        result.Page!.Items.Select(view => view.Idea.Id).ShouldBe([hot.Id]);
    }

    // Ideas fired a minute apart, the first the newest, so by equal expectancy they come in the order of their numbers.
    private void Ideas(int count)
    {
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, count).Select(number => Idea(number))]);
    }

    private static Idea Idea(int number, IdeaSetup setup = IdeaSetup.Impulse, IdeaSide side = IdeaSide.Long, IdeaStatus status = IdeaStatus.AwaitingEntry)
    {
        return IdeaGenerator.Generate(Noon.AddHours(-12).AddMinutes(-number), status, setup: setup, side: side) with { Id = Id(number) };
    }

    private static Guid Id(int number)
    {
        return new Guid(number, 0, 0, new byte[8]);
    }

    private static IdeaBucket Bucket(Idea idea, decimal expectancy)
    {
        var key = new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);

        return new IdeaBucket(key, 90, IdeaViewGenerator.Bucket(expectancy, 0.5m));
    }

    private SavedFilter Filter(params FilterCondition[] conditions)
    {
        var filter = new SavedFilter(Guid.NewGuid(), "Filter", SavedFilterPlace.Both, conditions, Noon);
        _filters.GetAsync(filter.Id, Arg.Any<CancellationToken>()).Returns(filter);

        return filter;
    }
}