using Npgsql;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class ScannerStateRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private NpgsqlDataSource _dataSource = null!;
    private ScannerStateRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new ScannerStateRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task First_scanner_takes_the_free_lease()
    {
        (await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None)).ShouldBeTrue();
    }

    // Scenario "Два сканера".
    [Test]
    public async Task Second_scanner_does_not_take_a_live_lease()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(10), Now.AddSeconds(10) + Lease, CancellationToken.None)).ShouldBeFalse();
    }

    // Scenario "Держатель упал".
    [Test]
    public async Task Another_scanner_takes_an_expired_lease()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.ClaimAsync(Guid.NewGuid(), Now.AddSeconds(31), Now.AddSeconds(31) + Lease, CancellationToken.None)).ShouldBeTrue();
    }

    // Decision 2: a holder that ends its session takes its own lease again without waiting for it to expire.
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
    public async Task Scanner_does_not_renew_a_lease_of_another()
    {
        await _store.ClaimAsync(Guid.NewGuid(), Now, Now + Lease, CancellationToken.None);

        (await _store.RenewAsync(Guid.NewGuid(), Now.AddSeconds(10) + Lease, CancellationToken.None)).ShouldBeFalse();
    }

    [Test]
    public async Task Scanner_that_never_ran_has_no_snapshot_and_no_pairs()
    {
        (await _store.GetAsync(CancellationToken.None)).ShouldBe(new ScannerState(null, 0, 0, null));
    }

    // Scenario "Bybit ответил 403": the state shows the end of the pause.
    [Test]
    public async Task Pause_of_rest_is_read_back()
    {
        await _store.PauseRestAsync(Now.AddMinutes(10), CancellationToken.None);

        (await _store.GetAsync(CancellationToken.None)).RestPausedUntil.ShouldBe(Now.AddMinutes(10));
    }

    // A short pause after a limit that comes after a ban of the IP leaves the end of the longer pause.
    [Test]
    public async Task Shorter_pause_keeps_the_later_end()
    {
        await _store.PauseRestAsync(Now.AddMinutes(10), CancellationToken.None);
        await _store.PauseRestAsync(Now.AddSeconds(30), CancellationToken.None);

        (await _store.GetAsync(CancellationToken.None)).RestPausedUntil.ShouldBe(Now.AddMinutes(10));
    }

    // Scenario "Загрузка истории идёт".
    [Test]
    public async Task Progress_of_the_history_load_is_read_back()
    {
        await _store.SetProgressAsync(1104, 312, CancellationToken.None);

        (await _store.GetAsync(CancellationToken.None)).ShouldBe(new ScannerState(null, 1104, 312, null));
    }
}