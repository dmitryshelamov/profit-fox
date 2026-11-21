using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Commands.Runs;

namespace ProfitFox.Tests.Unit.Runs;

/// <summary>
///     Spec bench-run-queue «Аренда прогона»: the Worker renews the lease of its run while it works and stops the work once it
///     cannot hold the lease. The keeper runs on a fake clock.
/// </summary>
public sealed class LeaseKeeperTests
{
    // A lease of 30 seconds renewed every 10.
    private readonly WorkerSettings _settings = new WorkerSettings();
    private readonly FakeTimeProvider _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-20T12:30:00Z"));

    [Test]
    public async Task The_lease_is_renewed_every_renewal_interval_while_the_work_runs()
    {
        int renewals = 0;
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync(_ => Task.FromResult(++renewals > 0), work, done.Token);

        await Advance(TimeSpan.FromSeconds(30));
        await done.CancelAsync();
        await keeping;

        Assert.That(renewals, Is.EqualTo(3));
        Assert.That(work.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task A_lease_another_Worker_took_cancels_the_work()
    {
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync(_ => Task.FromResult(false), work, done.Token);

        await Advance(TimeSpan.FromSeconds(10));
        await keeping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(work.IsCancellationRequested, Is.True);
    }

    // A database error on one renewal must neither leave the work running without a lease nor stop it while the lease holds.
    [Test]
    public async Task A_renewal_that_failed_is_tried_again_and_the_work_goes_on_once_it_succeeds()
    {
        int calls = 0;
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync(_ => ++calls == 1 ? throw new InvalidOperationException("The database is down.") : Task.FromResult(true), work, done.Token);

        await Advance(TimeSpan.FromSeconds(60));
        await done.CancelAsync();
        await keeping;

        Assert.That(calls, Is.EqualTo(6));
        Assert.That(work.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task The_work_stops_before_its_lease_expires_when_no_renewal_succeeds()
    {
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync(_ => throw new InvalidOperationException("The database is down."), work, done.Token);

        await Advance(TimeSpan.FromSeconds(10));
        bool afterFirst = work.IsCancellationRequested;
        await Advance(TimeSpan.FromSeconds(10));
        await keeping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(afterFirst, Is.False, "the lease of 30 seconds still holds after the first failure");
        Assert.That(work.IsCancellationRequested, Is.True, "the next try would come at the end of the lease");
    }

    [Test]
    public async Task The_keeper_ends_quietly_when_the_work_ends_between_two_renewals()
    {
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync(_ => Task.FromResult(true), work, done.Token);

        await Advance(TimeSpan.FromSeconds(5));
        await done.CancelAsync();

        Assert.That(async () => await keeping.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
        Assert.That(work.IsCancellationRequested, Is.False);
    }

    private Task KeepAsync(Func<CancellationToken, Task<bool>> renew, CancellationTokenSource work, CancellationToken done)
    {
        return LeaseKeeper.KeepAsync(renew, _settings, _time, work, done);
    }

    // Moves the clock a second at a time, so that each renewal runs before the next one is due.
    private async Task Advance(TimeSpan span)
    {
        for (TimeSpan moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
    }
}
