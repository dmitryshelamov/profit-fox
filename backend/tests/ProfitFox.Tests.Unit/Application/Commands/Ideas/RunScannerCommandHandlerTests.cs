using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Commands.Ideas.RunScanner;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Unit.Application.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Commands.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 18.12: between the feed and the scanner the queue is bounded — while the scanner is
///     slow the feed is not read on, and no minute is lost.
/// </summary>
public sealed class RunScannerCommandHandlerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-2) - TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerMinute);

    [Test]
    public async Task A_slow_scanner_stops_the_reading_of_the_feed_and_loses_no_minute()
    {
        const int minutes = 2000;
        var feed = new EndlessFeed(minutes);
        var market = new HeldMarket();
        var rig = new ScannerRig();
        var settings = new ScannerSettings { TickInterval = TimeSpan.FromMilliseconds(10), QueueCapacity = 100 };
        var handler = new RunScannerCommandHandler(
            feed,
            rig.History,
            rig.Ideas,
            market,
            new Lease(),
            new IdeaLibrary(rig.Strategies, rig.Signals, new MemoryIdeaSourceRepository(rig.Strategies, rig.Signals, rig.Enabled, rig.Labels)),
            TimeProvider.System,
            settings,
            new IdeasSettings(),
            NullLogger<RunScannerCommandHandler>.Instance);
        using var stop = new CancellationTokenSource();

        Task<bool> running = handler.Handle(new RunScannerCommand("w-1"), stop.Token);
        await market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        feed.Go.TrySetResult();
        await Task.Delay(500);
        int pulled = feed.Pulled;
        market.Release.TrySetResult();
        await Until(() => market.Pairs.FirstOrDefault()?.Price == minutes);
        await stop.CancelAsync();
        await running;

        Assert.That(pulled, Is.LessThanOrEqualTo(settings.QueueCapacity + 3), "the feed is not read on while the scanner waits");
        Assert.That(feed.Pulled, Is.EqualTo(minutes + 1), "every minute came through, the last one too");
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    // The set of one pair and its minutes one after another as fast as they are taken, after the first one only once the
    // test says so; how many were taken.
    private sealed class EndlessFeed(int count) : IMarketFeed
    {
        private int _pulled;

        public TaskCompletionSource Go { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Pulled => Volatile.Read(ref _pulled);

        public async IAsyncEnumerable<MarketEvent> Subscribe(MarketSubscription subscription, [EnumeratorCancellation] CancellationToken cancellation)
        {
            Interlocked.Increment(ref _pulled);
            yield return new MarketPairs([new Instrument("btc", "BTCUSDT")]);
            for (int minute = 1; minute <= count; minute++)
            {
                if (minute == 2)
                {
                    await Go.Task.WaitAsync(cancellation);
                }

                Interlocked.Increment(ref _pulled);
                yield return new LiveMinute("BTCUSDT", Start.AddMinutes(minute - count), minute, minute, minute, minute, 1, minute);
            }

            await Task.Delay(Timeout.Infinite, cancellation);
        }
    }

    // A snapshot store whose first write waits for the test.
    private sealed class HeldMarket : IMarketPairRepository
    {
        public TaskCompletionSource Entered { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<MarketPair> Pairs { get; private set; } = [];

        public async Task ReplaceAsync(IReadOnlyList<MarketPair> pairs, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Pairs = pairs;
        }

        public Task<ListPage<MarketPairRow>> ListAsync(MarketListQuery query, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MarketPairRow?> FindAsync(string symbol, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<FilterCount> CountAsync(IReadOnlyList<FilterCondition> conditions, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class Lease : IScannerLeaseRepository
    {
        public Task<bool> TakeAsync(string worker, TimeSpan duration, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(string worker, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReportAsync(string worker, DateTimeOffset? liveSince, int warmed, int pairs, CancellationToken cancellationToken, int failed = 0, string? failure = null)
        {
            return Task.CompletedTask;
        }

        public Task<(ScannerState State, DateTimeOffset Now)?> StateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<(ScannerState, DateTimeOffset)?>(null);
        }
    }
}
