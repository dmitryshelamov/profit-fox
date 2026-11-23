using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Logging;
using ProfitFox.Tests.Unit.Application.Commands.Signals;

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

    private static GetPairChartQueryHandler Handler(IRecentHistory history, ListLogger log)
    {
        return new GetPairChartQueryHandler(new ListedPairs(), history, new FakeTimeProvider(Now), new Typed(log));
    }

    // The journal of the handler, kept by a list.
    private sealed class Typed(ListLogger inner) : ILogger<GetPairChartQueryHandler>
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

        public (DateTimeOffset From, DateTimeOffset To)? FundingWindow { get; private set; }

        public async Task<RecentRead<HistoryCandle>> Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
        {
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
            await Task.Yield();
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

        public Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
        {
            return Task.FromResult(new RecentRead<OpenInterestPoint>(Loading: false, []));
        }
    }
}
