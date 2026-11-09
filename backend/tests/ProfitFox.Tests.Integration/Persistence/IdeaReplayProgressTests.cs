using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Decision 4 of speed-up-ideas-replay: the progress of the replay of a version of the rules is counted on reading, over the pairs the
///     replay takes (listed, with their history, coverage and price step) and their cursors of that version.
/// </summary>
public class IdeaReplayProgressTests
{
    private static readonly DateTimeOffset March10 = DateTimeOffset.Parse("2024-03-10T00:00:00Z");

    // The day ceiling of now less the days of history, before which the replay starts no pair.
    private static readonly DateTimeOffset HistoryStart = March10.AddDays(-90);

    private NpgsqlDataSource _dataSource = null!;
    private IdeaReplayRepository _cursors = null!;
    private ScannerPairRepository _pairs = null!;

    [SetUp]
    public async Task SetUp()
    {
        // A session in a time zone ahead of UTC: the day of the coverage is still the day in UTC.
        _dataSource = new NpgsqlDataSourceBuilder(IntegrationEnvironment.ConnectionString + ";Timezone=Asia/Tokyo").Build();
        _cursors = new IdeaReplayRepository(_dataSource);
        _pairs = new ScannerPairRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Прогон идёт": a pair with its cursor on the day of its coverage is replayed; one behind it is not and gives the slowest day; one
    // without a cursor of the version is not replayed either; a pair the replay does not take is not counted.
    [Test]
    public async Task Progress_counts_the_pairs_replayed_up_to_their_coverage_and_the_slowest_day()
    {
        await _pairs.SaveAsync(
        [
            Pair("WIFUSDT", March10.AddHours(20)),
            Pair("PEPEUSDT", March10.AddHours(12)),
            Pair("SOLUSDT", March10.AddHours(12)),
            Pair("XRPUSDT", March10.AddHours(12)),
            Pair("ETHUSDT", March10.AddHours(12)) with { TickSize = null },
            Pair("DOGEUSDT", March10.AddHours(12)) with { Listed = false },
            Pair("ADAUSDT", March10.AddHours(12)) with { HistoryFrom = null }
        ], CancellationToken.None);
        await Cursor("WIFUSDT", March10, IdeasEngineVersion.Current);
        await Cursor("PEPEUSDT", March10.AddDays(-2), IdeasEngineVersion.Current);
        await Cursor("XRPUSDT", March10, IdeasEngineVersion.Current - 1);
        await Cursor("ETHUSDT", March10.AddDays(-5), IdeasEngineVersion.Current);
        await Cursor("DOGEUSDT", March10.AddDays(-5), IdeasEngineVersion.Current);

        IdeaReplayProgress progress = await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None);

        progress.ShouldBe(new IdeaReplayProgress(1, 4, March10.AddDays(-2), IdeasEngineVersion.Current));
    }

    // Scenario "Прогон догнал все пары": no slowest day once every pair is replayed up to its coverage.
    [Test]
    public async Task Progress_of_a_replay_that_caught_up_has_no_slowest_day()
    {
        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12)), Pair("SOLUSDT", March10)], CancellationToken.None);
        await Cursor("WIFUSDT", March10, IdeasEngineVersion.Current);
        await Cursor("SOLUSDT", March10, IdeasEngineVersion.Current);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(2, 2, null, IdeasEngineVersion.Current));
    }

    // Scenario "Прогон ещё не начинался": a new version without cursors has replayed none of the pairs; an empty database has none to replay.
    [Test]
    public async Task Progress_of_a_version_without_cursors_is_none_of_the_pairs()
    {
        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(0, 0, null, IdeasEngineVersion.Current));

        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12))], CancellationToken.None);
        await Cursor("WIFUSDT", March10, IdeasEngineVersion.Current - 1);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(0, 1, null, IdeasEngineVersion.Current));
    }

    // A pair at most a day behind the day of its coverage is done: right after midnight every pair has one new day to come and stays done.
    [Test]
    public async Task Progress_counts_a_pair_a_day_behind_its_coverage_as_replayed_and_two_days_as_not()
    {
        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12)), Pair("SOLUSDT", March10.AddHours(12))], CancellationToken.None);
        await Cursor("WIFUSDT", March10.AddDays(-1), IdeasEngineVersion.Current);
        await Cursor("SOLUSDT", March10.AddDays(-2), IdeasEngineVersion.Current);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None))
            .ShouldBe(new IdeaReplayProgress(1, 2, March10.AddDays(-2), IdeasEngineVersion.Current));
    }

    // A pair listed today has no full day to replay and takes no turn: counted, done, and not the slowest.
    [Test]
    public async Task Progress_counts_a_pair_listed_today_as_replayed()
    {
        await _pairs.SaveAsync([Pair("NEWUSDT", March10.AddHours(12)) with { HistoryFrom = March10.AddHours(5) }], CancellationToken.None);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(1, 1, null, IdeasEngineVersion.Current));
    }

    // A coverage older than the history window leaves nothing to replay.
    [Test]
    public async Task Progress_counts_a_pair_covered_only_before_the_history_window_as_replayed()
    {
        await _pairs.SaveAsync([Pair("OLDUSDT", HistoryStart.AddDays(-5))], CancellationToken.None);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(1, 1, null, IdeasEngineVersion.Current));
    }

    // A cursor older than the history window starts over from the window's start, which is then the slowest day.
    [Test]
    public async Task Progress_of_a_cursor_older_than_the_history_window_starts_from_the_window()
    {
        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12))], CancellationToken.None);
        await Cursor("WIFUSDT", HistoryStart.AddDays(-3), IdeasEngineVersion.Current);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, HistoryStart, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(0, 1, HistoryStart, IdeasEngineVersion.Current));
    }

    private static ScannerPair Pair(string symbol, DateTimeOffset coveredUntil)
    {
        return new ScannerPair(new Instrument(MarketCategory.Linear, symbol), true, HistoryFrom: March10.AddDays(-90), CoveredUntil: coveredUntil, TickSize: 0.0001m);
    }

    private Task Cursor(string symbol, DateTimeOffset replayedUntil, int version)
    {
        return _cursors.SetAsync(new Instrument(MarketCategory.Linear, symbol), version, new IdeaReplayCursor(replayedUntil, []), CancellationToken.None);
    }
}