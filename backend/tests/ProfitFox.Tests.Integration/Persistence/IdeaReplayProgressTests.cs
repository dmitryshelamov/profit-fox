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

        IdeaReplayProgress progress = await _cursors.ProgressAsync(IdeasEngineVersion.Current, CancellationToken.None);

        progress.ShouldBe(new IdeaReplayProgress(1, 4, March10.AddDays(-2), IdeasEngineVersion.Current));
    }

    // Scenario "Прогон догнал все пары": no slowest day once every pair is replayed up to its coverage.
    [Test]
    public async Task Progress_of_a_replay_that_caught_up_has_no_slowest_day()
    {
        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12)), Pair("SOLUSDT", March10)], CancellationToken.None);
        await Cursor("WIFUSDT", March10, IdeasEngineVersion.Current);
        await Cursor("SOLUSDT", March10, IdeasEngineVersion.Current);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(2, 2, null, IdeasEngineVersion.Current));
    }

    // Scenario "Прогон ещё не начинался": a new version without cursors has replayed none of the pairs; an empty database has none to replay.
    [Test]
    public async Task Progress_of_a_version_without_cursors_is_none_of_the_pairs()
    {
        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(0, 0, null, IdeasEngineVersion.Current));

        await _pairs.SaveAsync([Pair("WIFUSDT", March10.AddHours(12))], CancellationToken.None);
        await Cursor("WIFUSDT", March10, IdeasEngineVersion.Current - 1);

        (await _cursors.ProgressAsync(IdeasEngineVersion.Current, CancellationToken.None)).ShouldBe(new IdeaReplayProgress(0, 1, null, IdeasEngineVersion.Current));
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