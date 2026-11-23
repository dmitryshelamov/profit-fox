using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Services.Market;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Unit.Application.Services.Market;

/// <summary>
///     Change add-ideas-and-market, task 23.2: the reads of the page of a pair have a limit of their own, 10 seconds an
///     attempt; a step longer than 5 seconds and every limit expired are a line of the journal of level <c>Warning</c> with
///     the step, the pair and the time, no more than once a minute for each step.
/// </summary>
public sealed class PageReadsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 23, 12, 34, 0, TimeSpan.Zero);

    [Test]
    public async Task An_attempt_that_does_not_end_in_ten_seconds_is_out_of_time_whatever_it_does_with_its_token()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new ListLogger();
        var reads = new PageReads(clock, new Typed(log));
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> read = reads.Run("candles", "BTCUSDT", _ => Hang(asked), CancellationToken.None);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(PageReads.Limit - TimeSpan.FromMilliseconds(1));
        bool waitsBefore = !read.IsCompleted;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Exception? outOfTime = await Refusal(read);

        Assert.That(waitsBefore, Is.True, "the attempt has its ten seconds");
        Assert.That(outOfTime, Is.TypeOf<ReadTimedOutException>());
        Assert.That(((ReadTimedOutException)outOfTime!).Step, Is.EqualTo("candles"));
        Assert.That(Warnings(log), Has.Count.EqualTo(1));
        Assert.That(Warnings(log)[0], Does.Contain("candles").And.Contain("BTCUSDT").And.Contain("10000 ms"));
    }

    [Test]
    public async Task A_step_longer_than_five_seconds_is_written_once_a_minute_for_each_step()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new ListLogger();
        var reads = new PageReads(clock, new Typed(log));

        await Slow(reads, clock, "candles", "BTCUSDT", TimeSpan.FromSeconds(6));
        await Slow(reads, clock, "candles", "ETHUSDT", TimeSpan.FromSeconds(6));
        await Slow(reads, clock, "funding", "ETHUSDT", TimeSpan.FromSeconds(6));
        await Slow(reads, clock, "candles", "BTCUSDT", PageReads.Slow);
        Assert.That(Warnings(log), Has.Count.EqualTo(2), "one for candles, one for funding; five seconds are not longer than five");
        Assert.That(Warnings(log)[0], Does.Contain("candles").And.Contain("BTCUSDT").And.Contain("6000 ms"));
        Assert.That(Warnings(log)[1], Does.Contain("funding").And.Contain("ETHUSDT"));

        clock.Advance(PageReads.WarningEvery);
        await Slow(reads, clock, "candles", "SOLUSDT", TimeSpan.FromSeconds(7));
        Assert.That(Warnings(log), Has.Count.EqualTo(3), "a minute later");
        Assert.That(Warnings(log)[2], Does.Contain("SOLUSDT").And.Contain("7000 ms"));
    }

    [Test]
    public async Task Every_step_out_of_time_is_written_once_a_minute_for_that_step()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new ListLogger();
        var reads = new PageReads(clock, new Typed(log));

        await OutOfTime(reads, clock, "oi-history", "BTCUSDT");
        await OutOfTime(reads, clock, "oi-history", "ETHUSDT");
        await OutOfTime(reads, clock, "oi-rest", "ETHUSDT");
        Assert.That(Warnings(log), Has.Count.EqualTo(2));
        Assert.That(Warnings(log)[1], Does.Contain("oi-rest").And.Contain("ETHUSDT"));

        clock.Advance(PageReads.WarningEvery);
        await OutOfTime(reads, clock, "oi-history", "SOLUSDT");
        Assert.That(Warnings(log), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task A_request_cancelled_is_cancelled_and_not_written()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new ListLogger();
        var reads = new PageReads(clock, new Typed(log));
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gone = new CancellationTokenSource();

        Task<int> read = reads.Run("candles", "BTCUSDT", _ => Hang(asked), gone.Token);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(6));
        await gone.CancelAsync();
        Exception? cancelled = await Refusal(read);

        Assert.That(cancelled, Is.InstanceOf<OperationCanceledException>());
        Assert.That(Warnings(log), Is.Empty, "neither out of time nor a slow step");
    }

    [Test]
    public async Task A_failure_within_the_limit_is_the_failure()
    {
        var reads = new PageReads(new FakeTimeProvider(Now));

        Exception? failure = await Refusal(reads.Run<int>("catalog", "BTCUSDT", _ => throw new HistoryUnavailableException("The instrument catalog did not answer."), CancellationToken.None));

        Assert.That(failure, Is.TypeOf<HistoryUnavailableException>());
    }

    private static async Task Slow(PageReads reads, FakeTimeProvider clock, string step, string symbol, TimeSpan took)
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> read = reads.Run(step, symbol, async _ =>
        {
            asked.SetResult();
            return await gate.Task;
        }, CancellationToken.None);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(took);
        gate.SetResult(1);
        await read.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task OutOfTime(PageReads reads, FakeTimeProvider clock, string step, string symbol)
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> read = reads.Run(step, symbol, _ => Hang(asked), CancellationToken.None);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(PageReads.Limit);
        Assert.That(await Refusal(read), Is.TypeOf<ReadTimedOutException>());
    }

    // A read that never ends, whatever its token says: a service that hangs.
    private static Task<int> Hang(TaskCompletionSource asked)
    {
        asked.TrySetResult();
        return new TaskCompletionSource<int>().Task;
    }

    private static async Task<Exception?> Refusal(Task<int> read)
    {
        try
        {
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static List<string> Warnings(ListLogger log)
    {
        return [.. log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)];
    }

    // The journal of the reads, kept by a list.
    private sealed class Typed(ListLogger inner) : ILogger<PageReads>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return inner.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
