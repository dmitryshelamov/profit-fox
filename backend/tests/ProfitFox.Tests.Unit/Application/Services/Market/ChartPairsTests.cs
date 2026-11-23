using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.Market;

/// <summary>
///     Change add-ideas-and-market, task 21.5: the list of instruments of the requests of the chart of a pair is kept in
///     memory for a minute — one read for all, the requests that come while it is read wait for it — and a pair the list
///     kept does not have reads it again. Task 22.2: the read is one for all who wait, with its own cancellation, and a
///     failure reaches them all at once and is remembered for <see cref="ChartPairs.Remember" />.
/// </summary>
public sealed class ChartPairsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 23, 12, 34, 0, TimeSpan.Zero);

    [Test]
    public async Task Requests_within_a_minute_read_the_list_once()
    {
        var catalog = new CountedCatalog("BTCUSDT", "ETHUSDT");
        var clock = new FakeTimeProvider(Now);
        var pairs = new ChartPairs(catalog, clock);

        Instrument? first = await pairs.Find("BTCUSDT", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(59));
        Instrument? second = await pairs.Find("ETHUSDT", CancellationToken.None);

        Assert.That((first?.Id, second?.Id), Is.EqualTo(("BTCUSDT-id", "ETHUSDT-id")));
        Assert.That(catalog.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task The_list_is_read_again_a_minute_later()
    {
        var catalog = new CountedCatalog("BTCUSDT");
        var clock = new FakeTimeProvider(Now);
        var pairs = new ChartPairs(catalog, clock);

        await pairs.Find("BTCUSDT", CancellationToken.None);
        clock.Advance(ChartPairs.Keep);
        await pairs.Find("BTCUSDT", CancellationToken.None);

        Assert.That(catalog.Reads, Is.EqualTo(2));
    }

    [Test]
    public async Task Requests_that_come_while_the_list_is_read_wait_for_that_read()
    {
        var catalog = new CountedCatalog("BTCUSDT", "ETHUSDT") { Gate = new TaskCompletionSource() };
        var pairs = new ChartPairs(catalog, new FakeTimeProvider(Now));

        Task<Instrument?>[] finds = [.. new[] { "BTCUSDT", "ETHUSDT", "BTCUSDT" }.Select(symbol => pairs.Find(symbol, CancellationToken.None))];
        await Task.Delay(50);
        catalog.Gate.SetResult();
        Instrument?[] found = await Task.WhenAll(finds);

        Assert.That(found.Select(pair => pair?.Symbol), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT", "BTCUSDT" }));
        Assert.That(catalog.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task A_pair_the_list_kept_does_not_have_reads_it_again()
    {
        var catalog = new CountedCatalog("BTCUSDT");
        var pairs = new ChartPairs(catalog, new FakeTimeProvider(Now));
        await pairs.Find("BTCUSDT", CancellationToken.None);

        Instrument? missing = await pairs.Find("NOSUCHUSDT", CancellationToken.None);
        catalog.Symbols = ["BTCUSDT", "NEWUSDT"];
        Instrument? listed = await pairs.Find("NEWUSDT", CancellationToken.None);

        Assert.That(missing, Is.Null);
        Assert.That(listed?.Id, Is.EqualTo("NEWUSDT-id"), "a pair listed after the list was read");
        Assert.That(catalog.Reads, Is.EqualTo(3));
    }

    [Test]
    public async Task A_failed_read_refuses_all_who_wait_for_it_at_once()
    {
        var catalog = new CountedCatalog("BTCUSDT") { Gate = new TaskCompletionSource() };
        var pairs = new ChartPairs(catalog, new FakeTimeProvider(Now));

        Task<Instrument?>[] finds = [.. Enumerable.Range(0, 5).Select(_ => pairs.Find("BTCUSDT", CancellationToken.None))];
        await Task.Delay(50);
        catalog.Gate.SetException(new HistoryUnavailableException("The instrument catalog did not answer."));
        Exception?[] refusals = await Task.WhenAll(finds.Select(Refusal));

        Assert.That(refusals, Has.All.TypeOf<HistoryUnavailableException>());
        Assert.That(catalog.Reads, Is.EqualTo(1), "one read for the five requests");
    }

    [Test]
    public async Task A_failure_is_remembered_for_five_seconds()
    {
        var catalog = new CountedCatalog("BTCUSDT") { Failure = new HistoryUnavailableException("The instrument catalog did not answer.") };
        var clock = new FakeTimeProvider(Now);
        var pairs = new ChartPairs(catalog, clock);

        Exception? first = await Refusal(pairs.Find("BTCUSDT", CancellationToken.None));
        clock.Advance(ChartPairs.Remember - TimeSpan.FromMilliseconds(1));
        Exception? remembered = await Refusal(pairs.Find("BTCUSDT", CancellationToken.None));
        int readsWithin = catalog.Reads;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        catalog.Failure = null;
        Instrument? after = await pairs.Find("BTCUSDT", CancellationToken.None);

        Assert.That(ChartPairs.Remember, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(first, Is.TypeOf<HistoryUnavailableException>());
        Assert.That(remembered, Is.SameAs(first), "the same refusal within five seconds");
        Assert.That(readsWithin, Is.EqualTo(1), "no read within five seconds");
        Assert.That(after?.Id, Is.EqualTo("BTCUSDT-id"));
        Assert.That(catalog.Reads, Is.EqualTo(2), "a new read five seconds later");
    }

    [Test]
    public async Task Cancelling_one_request_does_not_cancel_the_read()
    {
        var catalog = new CountedCatalog("BTCUSDT") { Gate = new TaskCompletionSource() };
        var pairs = new ChartPairs(catalog, new FakeTimeProvider(Now));
        using var cancelled = new CancellationTokenSource();

        Task<Instrument?> first = pairs.Find("BTCUSDT", cancelled.Token);
        Task<Instrument?>[] others = [.. Enumerable.Range(0, 4).Select(_ => pairs.Find("BTCUSDT", CancellationToken.None))];
        await Task.Delay(50);
        await cancelled.CancelAsync();
        Exception? refusal = await Refusal(first);
        bool othersWait = others.All(find => !find.IsCompleted);
        catalog.Gate.SetResult();
        Instrument?[] found = await Task.WhenAll(others);

        Assert.That(refusal, Is.InstanceOf<OperationCanceledException>());
        Assert.That(othersWait, Is.True, "the others still wait for the read");
        Assert.That(found.Select(pair => pair?.Id), Has.All.EqualTo("BTCUSDT-id"));
        Assert.That(catalog.Reads, Is.EqualTo(1));
    }

    private static async Task<Exception?> Refusal(Task<Instrument?> find)
    {
        try
        {
            await find;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    ///     A catalog of the symbols given that counts its reads; with a gate, a read waits for it.
    /// </summary>
    public sealed class CountedCatalog(params string[] symbols) : IInstrumentCatalog
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public string[] Symbols { get; set; } = symbols;

        public TaskCompletionSource? Gate { get; init; }

        public Exception? Failure { get; set; }

        public async Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellation);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return [.. Symbols.Select(symbol => new Instrument($"{symbol}-id", symbol))];
        }
    }
}
