using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Requirement "Один держатель потока сделок" at the level of storage (decision 2 of collect-trade-flow).</summary>
public class TradeFlowStateRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private NpgsqlDataSource _dataSource = null!;
    private TradeFlowStateRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _store = new TradeFlowStateRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task First_collector_takes_the_free_lease()
    {
        (await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None)).ShouldBeTrue();
    }

    // Scenario "Два сборщика".
    [Test]
    public async Task Second_collector_does_not_take_a_live_lease()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(10), Now.AddSeconds(10) + Lease, CancellationToken.None)).ShouldBeFalse();
    }

    // Scenario "Держатель упал".
    [Test]
    public async Task Another_collector_takes_an_expired_lease()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(31), Now.AddSeconds(31) + Lease, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task Holder_takes_its_own_live_lease_again()
    {
        var holder = Guid.NewGuid();
        await _store.ClaimAsync(holder, Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(holder, Now.AddSeconds(10), Now.AddSeconds(10) + Lease, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task Holder_renews_its_lease()
    {
        var holder = Guid.NewGuid();
        await _store.ClaimAsync(holder, Now, Now + Lease, CancellationToken.None);

        bool renewed = await _store.RenewAsync(holder, Now.AddSeconds(10) + Lease, CancellationToken.None);
        bool takenMeanwhile = await _store.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(31), Now.AddSeconds(31) + Lease, CancellationToken.None);

        renewed.ShouldBeTrue();
        takenMeanwhile.ShouldBeFalse();
    }

    [Test]
    public async Task Collector_does_not_renew_a_lease_of_another()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.RenewAsync(Guid.NewGuid(), Now.AddSeconds(10) + Lease, CancellationToken.None)).ShouldBeFalse();
    }

    // The lease of the trade flow is apart from the lease of the scanner.
    [Test]
    public async Task Lease_of_the_scanner_does_not_hold_the_trade_flow()
    {
        await new ScannerStateRepository(_dataSource).ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None)).ShouldBeTrue();
    }
}