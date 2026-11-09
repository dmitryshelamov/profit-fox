using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Task 5.2 of tune-idea-rules: the signals of the replay, the buckets, the cursors of the replay and the percentiles of volatility.</summary>
public class IdeaSignalRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:05:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaSignalRepository _signals = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _signals = new IdeaSignalRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Every part of the path, the ladder with its gaps and the zone of «Уровни» among them.
    [Test]
    public async Task Signals_are_read_back_exactly()
    {
        IdeaSignal target = IdeaSignalGenerator.Generate(Now);
        IdeaSignal generated = IdeaSignalGenerator.Generate(Now, IdeaStatus.Stop, type: IdeaType.Levels);
        IdeaSignal levels = generated with { Path = generated.Path with { Target = 103.1234m, TargetAfter = 7, Entry = 100.123456789m, Tick = 0.0001m } };
        IdeaSignal stopEntry = IdeaSignalGenerator.Generate(Now, IdeaStatus.AwaitingEntry, type: IdeaType.StopEntry);
        IdeaSignal waiting = stopEntry with { Path = stopEntry.Path with { StopBeforeEntryAfter = 40 } };

        await _signals.UpsertAsync([target, levels, waiting], CancellationToken.None);

        (await _signals.GetAsync(IdeaGenerator.Wif, IdeasEngineVersion.Current, CancellationToken.None)).Select(IdeaSignalGenerator.Shape)
            .ShouldBe([.. new[] { levels, waiting, target }.Select(IdeaSignalGenerator.Shape)]);
    }

    // Task 4.2 of add-market-idea-features: a signal is read with the numbers of the market it fired with and so with their level; one without
    // them reads without features.
    [Test]
    public async Task Signals_are_read_back_with_their_numbers_of_the_market()
    {
        IdeaSignal marked = IdeaSignalGenerator.Generate(Now) with { PairChange = -0.0712345m, BtcChange = -0.0123m, Funding = 0.0006m };
        IdeaSignal plain = IdeaSignalGenerator.Generate(Now.AddMinutes(5));

        await _signals.UpsertAsync([marked, plain], CancellationToken.None);

        IReadOnlyList<IdeaSignal> read = await _signals.GetAsync(IdeaGenerator.Wif, IdeasEngineVersion.Current, CancellationToken.None);
        read.Select(signal => (signal.PairChange, signal.BtcChange, signal.Funding, signal.FeaturesWith(null))).ShouldBe(
        [
            (-0.0712345m, -0.0123m, 0.0006m, new IdeaFeatures(true, true, true)),
            (null, null, null, default)
        ]);
        (await _signals.StreamAsync(IdeasEngineVersion.Current, Now.AddMinutes(-1), null, CancellationToken.None).ToArrayAsync()).Select(IdeaSignalGenerator.Shape)
            .ShouldBe([.. new[] { marked, plain }.Select(IdeaSignalGenerator.Shape)]);
    }

    // Task 2.2 of add-burst-and-auto-limits: the distinct pairs of a side on an interval by minute. Three types of WIF and WIF spot and ETH in
    // one minute count three pairs of longs on 15 min; a short, another interval, another minute, another version and a signal before the
    // start are counted apart or not at all.
    [Test]
    public async Task Pairs_are_counted_by_the_minute_the_side_and_the_interval()
    {
        var spot = new Instrument(MarketCategory.Spot, "WIFUSDT");
        var eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
        IdeaSignal shortOne = IdeaSignalGenerator.Generate(Now, instrument: eth, type: IdeaType.BeyondExtreme);
        await _signals.UpsertAsync(
        [
            IdeaSignalGenerator.Generate(Now),
            IdeaSignalGenerator.Generate(Now, type: IdeaType.BeyondExtreme),
            IdeaSignalGenerator.Generate(Now, type: IdeaType.Pullback),
            IdeaSignalGenerator.Generate(Now, instrument: spot),
            IdeaSignalGenerator.Generate(Now, instrument: eth),
            shortOne with { Path = shortOne.Path with { Side = IdeaSide.Short } },
            IdeaSignalGenerator.Generate(Now, instrument: eth, interval: Interval.OneHour),
            IdeaSignalGenerator.Generate(Now.AddMinutes(1), instrument: eth),
            IdeaSignalGenerator.Generate(Now, instrument: new Instrument(MarketCategory.Linear, "SOLUSDT"), engineVersion: IdeasEngineVersion.Current - 1),
            IdeaSignalGenerator.Generate(Now.AddMinutes(-1), instrument: new Instrument(MarketCategory.Linear, "SOLUSDT"))
        ], CancellationToken.None);

        IReadOnlyDictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int> pairs = await _signals.CountPairsByMinuteAsync(IdeasEngineVersion.Current, Now, CancellationToken.None);

        pairs.ShouldBe(new Dictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int>
        {
            [(Now, IdeaSide.Long, Interval.FifteenMinutes)] = 3,
            [(Now, IdeaSide.Short, Interval.FifteenMinutes)] = 1,
            [(Now, IdeaSide.Long, Interval.OneHour)] = 1,
            [(Now.AddMinutes(1), IdeaSide.Long, Interval.FifteenMinutes)] = 1
        }, ignoreOrder: true);
    }

    // The pairs are counted a week of signals at a time, the weeks cut where the chunks of the table are (Thursday 00:00 UTC): the minutes on
    // both sides of a cut, the minute of the cut itself and a minute weeks earlier are each counted once, with every pair of theirs.
    [Test]
    public async Task Pairs_are_counted_once_on_both_sides_of_a_week()
    {
        var cut = DateTimeOffset.Parse("2024-03-07T00:00:00Z");
        var eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
        await _signals.UpsertAsync(
        [
            IdeaSignalGenerator.Generate(cut.AddDays(-21).AddMinutes(7)),
            IdeaSignalGenerator.Generate(cut.AddMinutes(-1)),
            IdeaSignalGenerator.Generate(cut.AddMinutes(-1), instrument: eth),
            IdeaSignalGenerator.Generate(cut),
            IdeaSignalGenerator.Generate(cut, instrument: eth),
            IdeaSignalGenerator.Generate(cut, type: IdeaType.Pullback),
            IdeaSignalGenerator.Generate(Now)
        ], CancellationToken.None);

        IReadOnlyDictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int> pairs = await _signals.CountPairsByMinuteAsync(
            IdeasEngineVersion.Current,
            cut.AddDays(-30),
            CancellationToken.None);

        pairs.ShouldBe(new Dictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int>
        {
            [(cut.AddDays(-21).AddMinutes(7), IdeaSide.Long, Interval.FifteenMinutes)] = 1,
            [(cut.AddMinutes(-1), IdeaSide.Long, Interval.FifteenMinutes)] = 2,
            [(cut, IdeaSide.Long, Interval.FifteenMinutes)] = 2,
            [(Now, IdeaSide.Long, Interval.FifteenMinutes)] = 1
        }, ignoreOrder: true);
    }

    [Test]
    public async Task No_pairs_are_counted_without_signals_of_the_version()
    {
        await _signals.UpsertAsync([IdeaSignalGenerator.Generate(Now, engineVersion: IdeasEngineVersion.Current - 1)], CancellationToken.None);

        IReadOnlyDictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int> pairs = await _signals.CountPairsByMinuteAsync(
            IdeasEngineVersion.Current,
            Now.AddDays(-30),
            CancellationToken.None);

        pairs.ShouldBeEmpty();
    }

    // Decision 4: a path written as far as it went is written again as it goes on, under its key.
    [Test]
    public async Task Signal_written_again_takes_the_path_it_has_gone_on_to()
    {
        IdeaSignal open = IdeaSignalGenerator.Generate(Now);
        IdeaSignal partial = open with { Path = open.Path with { LadderAfter = new int?[10], Closes = [100m], TrackedUntil = Now.AddMinutes(20) } };
        await _signals.UpsertAsync([partial], CancellationToken.None);

        await _signals.UpsertAsync([open], CancellationToken.None);

        (await _signals.GetAsync(IdeaGenerator.Wif, IdeasEngineVersion.Current, CancellationToken.None)).Select(IdeaSignalGenerator.Shape).ShouldBe([IdeaSignalGenerator.Shape(open)]);
    }

    // Decision 7: read one by one in the order of the key and the firing, since a moment, of all types or of one type × interval.
    [Test]
    public async Task Signals_are_streamed_by_key_and_firing()
    {
        var sol = new Instrument(MarketCategory.Spot, "SOLUSDT");
        IdeaSignal late = IdeaSignalGenerator.Generate(Now.AddHours(2));
        IdeaSignal early = IdeaSignalGenerator.Generate(Now);
        IdeaSignal drop = IdeaSignalGenerator.Generate(Now.AddHours(1), setup: IdeaSetup.Drop);
        IdeaSignal pullback = IdeaSignalGenerator.Generate(Now.AddHours(1), type: IdeaType.Pullback);
        IdeaSignal other = IdeaSignalGenerator.Generate(Now.AddMinutes(-1), instrument: sol);
        IdeaSignal old = IdeaSignalGenerator.Generate(Now.AddDays(-1));
        await _signals.UpsertAsync([late, early, drop, pullback, other, old, IdeaSignalGenerator.Generate(Now, engineVersion: 3)], CancellationToken.None);

        IdeaSignal[] all = await _signals.StreamAsync(IdeasEngineVersion.Current, Now.AddMinutes(-1), null, CancellationToken.None).ToArrayAsync();
        IdeaSignal[] twoR = await _signals.StreamAsync(IdeasEngineVersion.Current, Now.AddMinutes(-1), (IdeaType.TwoR, Interval.FifteenMinutes), CancellationToken.None).ToArrayAsync();

        all.Select(signal => (signal.Instrument, signal.Setup, signal.Path.Type, signal.Path.FiredAt)).ShouldBe(
        [
            (IdeaGenerator.Wif, IdeaSetup.Drop, IdeaType.TwoR, Now.AddHours(1)),
            (IdeaGenerator.Wif, IdeaSetup.Impulse, IdeaType.Pullback, Now.AddHours(1)),
            (IdeaGenerator.Wif, IdeaSetup.Impulse, IdeaType.TwoR, Now),
            (IdeaGenerator.Wif, IdeaSetup.Impulse, IdeaType.TwoR, Now.AddHours(2)),
            (sol, IdeaSetup.Impulse, IdeaType.TwoR, Now.AddMinutes(-1))
        ]);
        twoR.Select(signal => signal.Path.FiredAt).ShouldBe([Now.AddHours(1), Now, Now.AddHours(2), Now.AddMinutes(-1)]);
    }

    // Decision 6: a new version of the rules clears the signals of the old ones, and the cases of version 4 with them, an hour of them at a
    // time, from the first.
    [Test]
    public async Task Signals_of_other_versions_are_removed_an_hour_at_a_time()
    {
        await _signals.UpsertAsync(
        [
            IdeaSignalGenerator.Generate(Now, engineVersion: 2), IdeaSignalGenerator.Generate(Now.AddMinutes(59), engineVersion: 2),
            IdeaSignalGenerator.Generate(Now.AddHours(3), engineVersion: 2), IdeaSignalGenerator.Generate(Now, engineVersion: 3)
        ], CancellationToken.None);

        (await _signals.DeleteOtherVersionsAsync(3, CancellationToken.None)).ShouldBe(2);
        (await _signals.GetAsync(IdeaGenerator.Wif, 2, CancellationToken.None)).Select(signal => signal.Path.FiredAt).ShouldBe([Now.AddHours(3)]);
        (await _signals.DeleteOtherVersionsAsync(3, CancellationToken.None)).ShouldBe(1);
        (await _signals.DeleteOtherVersionsAsync(3, CancellationToken.None)).ShouldBe(0);

        (await _signals.GetAsync(IdeaGenerator.Wif, 2, CancellationToken.None)).ShouldBeEmpty();
        (await _signals.GetAsync(IdeaGenerator.Wif, 3, CancellationToken.None)).Count.ShouldBe(1);
    }

    // Review of tune-idea-rules: a pair replayed over from the start loses its signals of the version, of the other pairs and versions none.
    [Test]
    public async Task Signals_of_a_pair_of_a_version_are_removed()
    {
        var sol = new Instrument(MarketCategory.Spot, "SOLUSDT");
        await _signals.UpsertAsync(
            [IdeaSignalGenerator.Generate(Now), IdeaSignalGenerator.Generate(Now, engineVersion: 3), IdeaSignalGenerator.Generate(Now, instrument: sol)],
            CancellationToken.None);

        await _signals.DeleteAsync(IdeaGenerator.Wif, IdeasEngineVersion.Current, CancellationToken.None);

        (await _signals.GetAsync(IdeaGenerator.Wif, IdeasEngineVersion.Current, CancellationToken.None)).ShouldBeEmpty();
        (await _signals.GetAsync(IdeaGenerator.Wif, 3, CancellationToken.None)).Count.ShouldBe(1);
        (await _signals.GetAsync(sol, IdeasEngineVersion.Current, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Buckets_of_a_version_are_replaced_whole()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        var full = new IdeaBucket(Key(IdeaSetup.Impulse), 90, new BucketStatistics(400, 180, 200, 20, 0.45m, 0.40m, 0.50m, 0.12m, 0.002m, 0.238m));
        var few = new IdeaBucket(Key(IdeaSetup.Drop) with { Type = IdeaType.Pullback }, 30, new BucketStatistics(12, 5, 6, 1, null, null, null, -0.1m, null, null, 24));
        await buckets.ReplaceAsync(1, [new IdeaBucket(Key(IdeaSetup.EarlyDrop), 90, few.Statistics)], null, CancellationToken.None);

        await buckets.ReplaceAsync(2, [full, few], null, CancellationToken.None);

        (await buckets.GetAsync(2, CancellationToken.None)).ShouldBe([full, few], ignoreOrder: true);
        (await buckets.GetAsync(1, CancellationToken.None)).ShouldBeEmpty();
    }

    // Requirement "Пересчёт корзин": the recompute of one type × interval replaces only its buckets.
    [Test]
    public async Task Buckets_of_a_type_and_interval_are_replaced_alone()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        var twoR = new IdeaBucket(Key(IdeaSetup.Impulse), 90, new BucketStatistics(12, 5, 6, 1, null, null, null, -0.1m, null, null));
        IdeaBucket hour = twoR with { Key = twoR.Key with { Interval = Interval.OneHour } };
        IdeaBucket pullback = twoR with { Key = twoR.Key with { Type = IdeaType.Pullback } };
        await buckets.ReplaceAsync(2, [twoR, hour, pullback], null, CancellationToken.None);
        IdeaBucket recomputed = twoR with { Days = 30 };

        await buckets.ReplaceAsync(2, [recomputed], (IdeaType.TwoR, Interval.FifteenMinutes), CancellationToken.None);

        (await buckets.GetAsync(2, CancellationToken.None)).ShouldBe([recomputed, hour, pullback], ignoreOrder: true);
    }

    // Decision 4: the cursor keeps the paths of the replay still open at it, so the next turn goes on from them.
    [Test]
    public async Task Cursors_of_the_replay_are_kept_per_version_with_their_open_paths()
    {
        var replay = new IdeaReplayRepository(_dataSource);
        var sol = new Instrument(MarketCategory.Spot, "SOLUSDT");
        IdeaSignal generated = IdeaSignalGenerator.Generate(Now, engineVersion: 1);
        IdeaSignal open = generated with { Path = generated.Path with { Tick = 0.0001m, TrackedUntil = Now.AddHours(1) }, PairChange = -0.06m, Funding = 0.0005m };
        await replay.SetAsync(IdeaGenerator.Wif, 1, new IdeaReplayCursor(Now, [open]), CancellationToken.None);
        await replay.SetAsync(IdeaGenerator.Wif, 1, new IdeaReplayCursor(Now.AddDays(1), [open]), CancellationToken.None);
        await replay.SetAsync(sol, 2, new IdeaReplayCursor(Now, []), CancellationToken.None);

        IdeaReplayCursor cursor = (await replay.GetAsync(1, CancellationToken.None)).Single().Value;
        cursor.ReplayedUntil.ShouldBe(Now.AddDays(1));
        cursor.Open.Select(IdeaSignalGenerator.Shape).ShouldBe([IdeaSignalGenerator.Shape(open)]);
        await replay.DeleteOtherVersionsAsync(2, CancellationToken.None);
        (await replay.GetAsync(1, CancellationToken.None)).ShouldBeEmpty();
        (await replay.GetAsync(2, CancellationToken.None)).Keys.ShouldBe([sol]);
    }

    // The clean-up looks for signals of other versions only while a cursor of one is left.
    [Test]
    public async Task Cursor_of_another_version_is_told_from_the_cursors_of_the_version()
    {
        var replay = new IdeaReplayRepository(_dataSource);

        (await replay.HasOtherVersionsAsync(2, CancellationToken.None)).ShouldBeFalse();

        await replay.SetAsync(IdeaGenerator.Wif, 2, new IdeaReplayCursor(Now, []), CancellationToken.None);

        (await replay.HasOtherVersionsAsync(2, CancellationToken.None)).ShouldBeFalse();
        (await replay.HasOtherVersionsAsync(3, CancellationToken.None)).ShouldBeTrue();
    }

    // Decision 5: an applied rule goes over the one before it of its type × interval.
    [Test]
    public async Task Exit_rules_are_kept_per_type_and_interval()
    {
        var rules = new IdeaExitRuleRepository(_dataSource);
        var pullback = new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, Now);
        var levels = new IdeaExitRule(IdeaType.Levels, Interval.OneHour, 32, null, null, Now);
        await rules.SaveAsync(pullback with { LifeCandles = 24 }, CancellationToken.None);

        await rules.SaveAsync(pullback, CancellationToken.None);
        await rules.SaveAsync(levels, CancellationToken.None);

        (await rules.GetAllAsync(CancellationToken.None)).ShouldBe([pullback, levels], ignoreOrder: true);
    }

    [Test]
    public async Task Percentiles_of_volatility_are_replaced_per_pair_and_interval()
    {
        var volatility = new IdeaVolatilityRepository(_dataSource);
        await volatility.SaveAsync([new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.01m, 0.002m), new IdeaVolatility(IdeaGenerator.Wif, Interval.FiveMinutes, 0.002m, 0.0004m)], CancellationToken.None);

        await volatility.SaveAsync([new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.0125m, 0.0025m)], CancellationToken.None);

        (await volatility.GetAllAsync(CancellationToken.None)).ShouldBe(
            [new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.0125m, 0.0025m), new IdeaVolatility(IdeaGenerator.Wif, Interval.FiveMinutes, 0.002m, 0.0004m)],
            ignoreOrder: true);
    }

    // Migration plan, step 1: a row stored before the 10th percentile was kept reads without it, so the pair has no squeeze until a replay.
    [Test]
    public async Task Row_without_the_tenth_percentile_reads_without_it()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("""INSERT INTO idea_volatility (category, symbol, "interval", percentile) VALUES ('linear', 'WIFUSDT', '60', 0.01)""");
        }

        (await new IdeaVolatilityRepository(_dataSource).GetAllAsync(CancellationToken.None)).ShouldBe([new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.01m, null)]);
    }

    private static IdeaBucketKey Key(IdeaSetup setup)
    {
        return new IdeaBucketKey(setup, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
    }
}