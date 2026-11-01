using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Workers;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Workers;

/// <summary>Requirement "Аренда работы": the Worker renews its lease while it works and stops the work once it cannot hold it.</summary>
public class LeaseKeeperTests
{
    // A lease of 30 seconds renewed every 10.
    private readonly WorkerSettings _settings = new WorkerSettings();
    private readonly FakeTimeProvider _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:30:00Z"));

    [Test]
    public async Task Lease_is_renewed_every_renewal_interval_while_the_work_runs()
    {
        var renewals = new List<DateTimeOffset>();
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync((until, _) => Renewed(renewals, until, true), work, done.Token);

        await AdvanceAsync(TimeSpan.FromSeconds(30));
        await done.CancelAsync();
        await keeping;

        renewals.Count.ShouldBe(3);
        work.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Lease_taken_over_by_another_worker_cancels_the_work()
    {
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync((_, _) => Task.FromResult(false), work, done.Token);

        await AdvanceAsync(TimeSpan.FromSeconds(10));
        await keeping.WaitAsync(TimeSpan.FromSeconds(5));

        work.IsCancellationRequested.ShouldBeTrue();
    }

    // A database error on one renewal must not leave the work running without a lease or stop it while the lease still holds.
    [Test]
    public async Task Failed_renewal_is_tried_again_and_the_work_goes_on_once_it_succeeds()
    {
        var calls = 0;
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync((_, _) => ++calls == 1 ? throw new InvalidOperationException("The database is down.") : Task.FromResult(true), work, done.Token);

        await AdvanceAsync(TimeSpan.FromSeconds(60));
        await done.CancelAsync();
        await keeping;

        calls.ShouldBe(6);
        work.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Work_stops_before_its_lease_expires_when_no_renewal_succeeds()
    {
        using var work = new CancellationTokenSource();
        using var done = new CancellationTokenSource();
        Task keeping = KeepAsync((_, _) => throw new InvalidOperationException("The database is down."), work, done.Token);

        await AdvanceAsync(TimeSpan.FromSeconds(10));
        bool afterFirst = work.IsCancellationRequested;
        await AdvanceAsync(TimeSpan.FromSeconds(10));
        await keeping.WaitAsync(TimeSpan.FromSeconds(5));

        afterFirst.ShouldBeFalse();
        work.IsCancellationRequested.ShouldBeTrue();
    }

    private Task KeepAsync(Func<DateTimeOffset, CancellationToken, Task<bool>> renew, CancellationTokenSource work, CancellationToken done)
    {
        return LeaseKeeper.KeepAsync(renew, _settings, _time, work, done);
    }

    private static Task<bool> Renewed(List<DateTimeOffset> renewals, DateTimeOffset until, bool result)
    {
        renewals.Add(until);

        return Task.FromResult(result);
    }

    /// <summary>Moves the clock a second at a time, so that each renewal runs before the next one is due.</summary>
    private async Task AdvanceAsync(TimeSpan span)
    {
        for (TimeSpan moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
    }
}