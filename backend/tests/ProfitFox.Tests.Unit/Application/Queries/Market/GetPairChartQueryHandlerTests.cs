using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.Market;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Logging;
using ProfitFox.Tests.Unit.Application.Commands.Signals;
using ProfitFox.Tests.Unit.Application.Services.Market;

namespace ProfitFox.Tests.Unit.Application.Queries.Market;

/// <summary>
///     Change add-ideas-and-market, task 20.9: the page of the chart of a pair reads its candles and its payments together,
///     the payments over the window of the candles, and tells how long each step took.
/// </summary>
public sealed class GetPairChartQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 23, 12, 34, 0, TimeSpan.Zero);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    // The start of the window of the 500 candles of 1d before now: the boundary of today, 500 days back.
    private static readonly DateTimeOffset WindowStart = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero) - (500 * Day);

    [Test]
    public async Task The_candles_and_the_payments_are_read_together()
    {
        var history = new GatedHistory { CandlesWaitForFunding = true };
        GetPairChartQueryHandler handler = Handler(history, new ListLogger());

        PairChartInternal? chart = await handler.Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);

        Assert.That(chart!.Candles, Has.Count.EqualTo(500), "the candles waited for the payments to be asked: both go at once");
    }

    [Test]
    public async Task The_payments_are_read_over_the_window_of_the_candles_and_none_before_the_first_candle()
    {
        // A pair listed 100 days ago: its candles start later than the window, a payment before them is not on the chart.
        var history = new GatedHistory { FirstCandle = WindowStart + (400 * Day) };
        GetPairChartQueryHandler handler = Handler(history, new ListLogger());

        PairChartInternal? chart = await handler.Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);

        Assert.That(history.FundingWindow, Is.EqualTo((WindowStart, Now)));
        Assert.That(chart!.Candles, Has.Count.EqualTo(100));
        Assert.That(chart.Funding, Is.Not.Empty);
        Assert.That(chart.Funding.Select(payment => payment.Time), Has.All.GreaterThanOrEqualTo(WindowStart + (400 * Day)));
    }

    [Test]
    public async Task A_pair_whose_history_is_loading_is_loading_even_when_its_payments_fail()
    {
        var history = new GatedHistory { Loading = true, FundingFails = true };
        GetPairChartQueryHandler handler = Handler(history, new ListLogger());

        PairChartInternal? chart = await handler.Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);

        Assert.That(chart!.Loading, Is.True);
    }

    [Test]
    public async Task The_journal_tells_how_long_the_catalog_the_candles_and_the_payments_took()
    {
        var log = new ListLogger();
        GetPairChartQueryHandler handler = Handler(new GatedHistory(), log);

        await handler.Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);

        string line = log.Entries.Single(entry => entry.Level == LogLevel.Information).Message;
        Assert.That(line, Does.Contain("BTCUSDT").And.Contain("1d").And.Contain("catalog").And.Contain("candles").And.Contain("funding").And.Contain("ms"));
    }

    /// <summary>
    ///     Task 21.5: the requests of the chart find the pair in the list of instruments kept for a minute — two pages of the
    ///     chart and the open interest read the catalog once.
    /// </summary>
    [Test]
    public async Task Two_requests_of_the_chart_in_a_row_and_its_open_interest_read_the_catalog_once()
    {
        var catalog = new ChartPairsTests.CountedCatalog("BTCUSDT", "ETHUSDT");
        var clock = new FakeTimeProvider(Now);
        var pairs = new ChartPairs(catalog, clock);

        PairChartInternal? first = await new GetPairChartQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);
        PairChartInternal? second = await new GetPairChartQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairChartQuery("ETHUSDT", "1h", null), CancellationToken.None);
        PairOpenInterestInternal? interest = await new GetPairOpenInterestQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairOpenInterestQuery("BTCUSDT", "1d", null), CancellationToken.None);

        Assert.That(new object?[] { first, second, interest }, Has.None.Null);
        Assert.That(catalog.Reads, Is.EqualTo(1));
    }

    /// <summary>
    ///     Task 21.5: a pair the list kept does not have reads the catalog again, and is not found when it is not there.
    /// </summary>
    [Test]
    public async Task A_pair_not_in_the_list_kept_reads_the_catalog_again_and_is_not_found()
    {
        var catalog = new ChartPairsTests.CountedCatalog("BTCUSDT");
        var clock = new FakeTimeProvider(Now);
        var pairs = new ChartPairs(catalog, clock);
        await new GetPairChartQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);

        PairChartInternal? chart = await new GetPairChartQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairChartQuery("NOSUCHUSDT", "1d", null), CancellationToken.None);
        PairOpenInterestInternal? interest = await new GetPairOpenInterestQueryHandler(pairs, new GatedHistory(), clock).Handle(new GetPairOpenInterestQuery("NOSUCHUSDT", "1d", null), CancellationToken.None);

        Assert.That(new object?[] { chart, interest }, Has.All.Null);
        Assert.That(catalog.Reads, Is.EqualTo(3));
    }

    /// <summary>
    ///     Task 23.2: candles that do not come within the limit of the page answer that the chart loads — the page asks again
    ///     by its own timer — and the journal says which step, of which pair, did not end in time.
    /// </summary>
    [Test]
    public async Task Candles_that_do_not_come_within_ten_seconds_answer_that_the_chart_loads()
    {
        var log = new ListLogger();
        var clock = new FakeTimeProvider(Now);
        var history = new GatedHistory { CandlesHang = true };
        GetPairChartQueryHandler handler = Handler(history, clock, log);

        Task<PairChartInternal?> chart = handler.Handle(new GetPairChartQuery("BTCUSDT", "5m", null), CancellationToken.None);
        await history.CandlesAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(PageReads.Limit);
        PairChartInternal? answer = await chart.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(PageReads.Limit, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That((answer!.Loading, answer.Candles.Count), Is.EqualTo((true, 0)));
        // The payments are read alongside the candles: when their read ends after the clock has jumped, the journal also gets
        // the line of a slow step of the payments, so the line of the candles is looked for by its step.
        string[] warnings = [.. log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)];
        Assert.That(warnings, Has.One.Contains("candles").And.Contains("BTCUSDT").And.Contains("10"));
    }

    /// <summary>
    ///     Task 23.2: payments that do not come within the limit answer that the chart loads, as payments still loading do.
    /// </summary>
    [Test]
    public async Task Payments_that_do_not_come_within_ten_seconds_answer_that_the_chart_loads()
    {
        var log = new ListLogger();
        var clock = new FakeTimeProvider(Now);
        var history = new GatedHistory { FundingHangs = true };
        GetPairChartQueryHandler handler = Handler(history, clock, log);

        Task<PairChartInternal?> chart = handler.Handle(new GetPairChartQuery("BTCUSDT", "1d", null), CancellationToken.None);
        await history.FundingAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(PageReads.Limit);
        PairChartInternal? answer = await chart.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(answer!.Loading, Is.True);
        Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message), Has.One.Contains("funding").And.Contains("BTCUSDT"));
    }

    /// <summary>
    ///     Task 23.2: the open interest read under the limit of the page; an attempt that does not end in time answers that it
    ///     loads.
    /// </summary>
    [Test]
    public async Task Open_interest_that_does_not_come_within_ten_seconds_answers_that_it_loads()
    {
        var log = new ListLogger();
        var clock = new FakeTimeProvider(Now);
        var history = new GatedHistory { InterestHangs = true };
        var handler = new GetPairOpenInterestQueryHandler(new ChartPairs(new ListedPairs(), clock), history, clock, new PageReads(clock, new Typed<PageReads>(log)));

        Task<PairOpenInterestInternal?> interest = handler.Handle(new GetPairOpenInterestQuery("BTCUSDT", "5m", null), CancellationToken.None);
        await history.InterestAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(PageReads.Limit);
        PairOpenInterestInternal? answer = await interest.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That((answer!.Loading, answer.OpenInterest.Count), Is.EqualTo((true, 0)));
        Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message), Has.One.Contains("oi-history").And.Contains("BTCUSDT"));
    }

    /// <summary>
    ///     Task 23.2: a request the page dropped is cancelled, not out of time: it is not answered that it loads and not written.
    /// </summary>
    [Test]
    public async Task A_request_the_page_dropped_is_not_a_limit_that_expired()
    {
        var log = new ListLogger();
        var clock = new FakeTimeProvider(Now);
        var history = new GatedHistory { CandlesHang = true };
        GetPairChartQueryHandler handler = Handler(history, clock, log);
        using var gone = new CancellationTokenSource();

        Task<PairChartInternal?> chart = handler.Handle(new GetPairChartQuery("BTCUSDT", "5m", null), gone.Token);
        await history.CandlesAsked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await gone.CancelAsync();

        Assert.That(async () => await chart.WaitAsync(TimeSpan.FromSeconds(5)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning), Is.Empty);
    }

    private static GetPairChartQueryHandler Handler(IRecentHistory history, FakeTimeProvider clock, ListLogger log)
    {
        return new GetPairChartQueryHandler(new ChartPairs(new ListedPairs(), clock), history, clock, new Typed<GetPairChartQueryHandler>(log), new PageReads(clock, new Typed<PageReads>(log)));
    }

    private static GetPairChartQueryHandler Handler(IRecentHistory history, ListLogger log)
    {
        var clock = new FakeTimeProvider(Now);
        return new GetPairChartQueryHandler(new ChartPairs(new ListedPairs(), clock), history, clock, new Typed<GetPairChartQueryHandler>(log));
    }

    // The journal of a type, kept by a list.
    private sealed class Typed<T>(ListLogger inner) : ILogger<T>
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

    // Candles of 1d up to today and a payment every 8 hours over what was asked; the candles may wait until the payments
    // are asked, which a handler reading one after the other never does.
    private sealed class GatedHistory : IRecentHistory
    {
        private readonly TaskCompletionSource _fundingAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CandlesWaitForFunding { get; init; }

        public bool Loading { get; init; }

        public bool FundingFails { get; init; }

        public DateTimeOffset? FirstCandle { get; init; }

        // Task 23.2: reads that never end, whatever their token says, as a service that hangs.
        public bool CandlesHang { get; init; }

        public bool FundingHangs { get; init; }

        public bool InterestHangs { get; init; }

        public TaskCompletionSource CandlesAsked { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FundingAsked { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InterestAsked { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public (DateTimeOffset From, DateTimeOffset To)? FundingWindow { get; private set; }

        public async Task<RecentRead<HistoryCandle>> Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
        {
            CandlesAsked.TrySetResult();
            if (CandlesHang)
            {
                await new TaskCompletionSource().Task;
            }

            if (CandlesWaitForFunding)
            {
                await _fundingAsked.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            }

            if (Loading)
            {
                return RecentRead<HistoryCandle>.StillLoading;
            }

            var end = new DateTimeOffset(before.UtcTicks - (before.UtcTicks % interval.Ticks), TimeSpan.Zero);
            DateTimeOffset start = FirstCandle ?? end - (interval * limit);
            List<HistoryCandle> candles = [];
            for (DateTimeOffset open = start; open < end; open += interval)
            {
                candles.Add(new HistoryCandle(open, "99", "100", "98", "99.5", "10", Turnover: null, BuyVolume: null));
            }

            return new RecentRead<HistoryCandle>(Loading: false, candles);
        }

        public async Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
        {
            FundingWindow = (from, to);
            _fundingAsked.TrySetResult();
            FundingAsked.TrySetResult();
            await Task.Yield();
            if (FundingHangs)
            {
                await new TaskCompletionSource().Task;
            }

            if (FundingFails)
            {
                throw new ProfitFox.Application.Exceptions.HistoryUnavailableException("The REST of the market-data service did not answer.");
            }

            List<HistoryPayment> payments = [];
            for (DateTimeOffset time = from; time < to; time += TimeSpan.FromHours(8))
            {
                if (time < Now - TimeSpan.FromHours(8))
                {
                    payments.Add(new HistoryPayment(time, "0.0001"));
                }
            }

            return new RecentRead<HistoryPayment>(Loading: false, payments);
        }

        public async Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation, IReadSteps? steps = null)
        {
            InterestAsked.TrySetResult();
            if (InterestHangs && steps is not null)
            {
                return await steps.Run("oi-history", _ => new TaskCompletionSource<RecentRead<OpenInterestPoint>>().Task, cancellation);
            }

            return new RecentRead<OpenInterestPoint>(Loading: false, []);
        }
    }
}
