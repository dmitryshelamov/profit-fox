using System.Globalization;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Requirement "Снимок рынка" and decision 2: the snapshot is replaced whole, and only by the holder of the stream.</summary>
public class MarketSnapshotRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:05:10Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private static readonly PairMetrics Full = new PairMetrics(
        150.125m, 3m, 28.75m, -17.6m, 106m, -50m, 120000.5m, 1500000.123456789m, 200500m, 25m, -20m, -0.00012345m, 0.0123456789m);

    private static readonly PairMetrics Empty = new PairMetrics(64000.5m, null, null, null, null, null, null, null, null, null, null, null, 0.5m);

    private NpgsqlDataSource _dataSource = null!;
    private MarketSnapshotRepository _store = null!;
    private ScannerStateRepository _state = null!;
    private Guid _holder;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new MarketSnapshotRepository(_dataSource);
        _state = new ScannerStateRepository(_dataSource);
        _holder = Guid.NewGuid();
        await _state.ClaimAsync(_holder, Now, Now.AddSeconds(30), CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Rows_are_read_back_exactly_with_their_empty_metrics()
    {
        MarketSnapshotRow[] rows = [new MarketSnapshotRow(Sol, Now, Full, false), new MarketSnapshotRow(Btc, Now, Empty, true)];

        (await _store.ReplaceAsync(_holder, rows, Now, CancellationToken.None)).ShouldBeTrue();

        (await _store.GetAllAsync(CancellationToken.None)).ShouldBe(rows, ignoreOrder: true);
        (await _store.GetAsync(Btc, CancellationToken.None)).ShouldBe(rows[1]);
        (await _store.GetAsync(new Instrument(MarketCategory.Spot, "NOPEUSDT"), CancellationToken.None)).ShouldBeNull();
        (await _state.GetAsync(CancellationToken.None)).SnapshotAt.ShouldBe(Now);
    }

    [Test]
    public async Task New_snapshot_replaces_the_previous_one_whole()
    {
        await _store.ReplaceAsync(_holder, [new MarketSnapshotRow(Sol, Now, Full, false), new MarketSnapshotRow(Btc, Now, Empty, true)], Now, CancellationToken.None);

        await _store.ReplaceAsync(_holder, [new MarketSnapshotRow(Sol, Now.AddMinutes(1), Empty, false)], Now.AddMinutes(1), CancellationToken.None);

        (await _store.GetAllAsync(CancellationToken.None)).ShouldBe([new MarketSnapshotRow(Sol, Now.AddMinutes(1), Empty, false)]);
    }

    // Decision 2: a holder that fell behind does not write over the snapshot of the new one.
    [Test]
    public async Task Holder_that_lost_the_lease_does_not_write_the_snapshot()
    {
        await _store.ReplaceAsync(_holder, [new MarketSnapshotRow(Sol, Now, Full, false)], Now, CancellationToken.None);
        await _state.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(31), Now.AddSeconds(61), CancellationToken.None);

        bool written = await _store.ReplaceAsync(_holder, [new MarketSnapshotRow(Btc, Now.AddMinutes(1), Empty, true)], Now.AddMinutes(1), CancellationToken.None);

        written.ShouldBeFalse();
        (await _store.GetAllAsync(CancellationToken.None)).ShouldBe([new MarketSnapshotRow(Sol, Now, Full, false)]);
        (await _state.GetAsync(CancellationToken.None)).SnapshotAt.ShouldBe(Now);
    }
}