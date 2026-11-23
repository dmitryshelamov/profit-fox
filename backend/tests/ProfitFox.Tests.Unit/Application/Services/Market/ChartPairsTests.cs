using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.Market;

/// <summary>
///     Change add-ideas-and-market, task 21.5: the list of instruments of the requests of the chart of a pair is kept in
///     memory for a minute — one read for all, the requests that come while it is read wait for it — and a pair the list
///     kept does not have reads it again.
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

    /// <summary>
    ///     A catalog of the symbols given that counts its reads; with a gate, a read waits for it.
    /// </summary>
    public sealed class CountedCatalog(params string[] symbols) : IInstrumentCatalog
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public string[] Symbols { get; set; } = symbols;

        public TaskCompletionSource? Gate { get; init; }

        public async Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellation);
            }

            return [.. Symbols.Select(symbol => new Instrument($"{symbol}-id", symbol))];
        }
    }
}
