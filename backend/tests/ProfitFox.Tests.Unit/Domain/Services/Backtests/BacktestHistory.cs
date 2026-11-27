using System.Globalization;
using System.Text.Json;

using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>
///     The history of one pair for the tests of a backtest: candles of a timeframe from Monday 2026-01-05 at a price that
///     a test may write, the funding payments a test gives, and the minutes read on demand and counted. A hole leaves
///     minutes out of the coverage.
/// </summary>
internal sealed class BacktestHistory
{
    public static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset End { get; init; } = Monday.AddDays(21);

    /// <summary>
    ///     The payments of the pair; the first, at the start, is low, so the signal knows a rate below the threshold before
    ///     the first high one.
    /// </summary>
    public List<(DateTimeOffset Time, string Rate)> Payments { get; } = [(Monday, "0.0001")];

    public Func<DateTimeOffset, double> Price { get; init; } = _ => 100;

    public (DateTimeOffset From, DateTimeOffset To)? Hole { get; init; }

    /// <summary>
    ///     Every read of minutes, in order.
    /// </summary>
    public List<(DateTimeOffset From, DateTimeOffset To)> MinuteReads { get; } = [];

    /// <summary>
    ///     A payment of a high rate a minute before <paramref name="time" /> and of a low one at <paramref name="low" />: the
    ///     funding-short strategy gives an order at the close of the first candle that sees the high one, the candle closed at
    ///     <paramref name="time" />. A payment belongs to the candle that closes after it (change add-ideas-and-market, task
    ///     27.3); the position pays no payment before its entry either way, so the trades are those of a payment at the close.
    /// </summary>
    public BacktestHistory High(DateTimeOffset time, DateTimeOffset? low = null)
    {
        Payments.Add((time.AddMinutes(-1), "0.0006"));
        Payments.Add((low ?? time.AddHours(1), "0.0001"));
        Payments.Sort((a, b) => a.Time.CompareTo(b.Time));
        return this;
    }

    public IReadOnlyDictionary<TimeSpan, IReadOnlyList<HistoryCandle>> Candles(params TimeSpan[] timeframes)
    {
        return timeframes.ToDictionary(timeframe => timeframe, timeframe => (IReadOnlyList<HistoryCandle>)[.. CandlesOf(timeframe)]);
    }

    public HistoryRead<HistoryPayment> Funding()
    {
        return new HistoryRead<HistoryPayment>([new TimeRange(Monday, End.AddSeconds(1))], [.. Payments.Select(payment => new HistoryPayment(payment.Time, payment.Rate))]);
    }

    public Task<HistoryRead<HistoryCandle>> Minutes(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        MinuteReads.Add((from, to));
        List<TimeRange> coverage = Hole is { } hole && hole.From < to && hole.To > from
            ? [new TimeRange(from, hole.From), new TimeRange(hole.To, to)]
            : [new TimeRange(from, to)];
        List<HistoryCandle> candles = [];
        for (DateTimeOffset minute = from; minute < to; minute += TimeSpan.FromMinutes(1))
        {
            if (coverage.Any(range => range.From <= minute && minute < range.Until))
            {
                candles.Add(Candle(minute));
            }
        }

        return Task.FromResult(new HistoryRead<HistoryCandle>(coverage, candles));
    }

    /// <summary>
    ///     The sets of <paramref name="strategy" /> of the kind on this history with the values and timeframes given.
    /// </summary>
    public IReadOnlyList<BacktestPlan> Plans(string strategy, StrategyKind kind, IReadOnlyList<ParameterValues> values, params TimeSpan[] timeframes)
    {
        using var document = JsonDocument.Parse(strategy);
        GraphDefinition graph = StrategyGraph.ReadForm(document.RootElement).Value!;
        var settings = new BacktestSettings(values, timeframes, Monday, End, Pairs: null, 0.00055, 0.0002);
        return BacktestPlans.Of(graph, kind, settings, BacktestGrid.Of(settings));
    }

    public Task<IReadOnlyList<PairSetResult>> Run(StrategyKind kind, IReadOnlyList<BacktestPlan> plans)
    {
        TimeSpan[] timeframes = [.. plans.Select(plan => plan.Design.Timeframe).Distinct()];
        return PairBacktest.RunAsync("BTCUSDT", kind, plans, Candles(timeframes), Funding(), Minutes, CancellationToken.None);
    }

    private IEnumerable<HistoryCandle> CandlesOf(TimeSpan timeframe)
    {
        for (DateTimeOffset open = Monday; open + timeframe <= End; open += timeframe)
        {
            yield return Candle(open);
        }
    }

    private HistoryCandle Candle(DateTimeOffset open)
    {
        string price = Price(open).ToString(CultureInfo.InvariantCulture);
        return new HistoryCandle(open, price, price, price, price, "1", Turnover: null, BuyVolume: null);
    }
}
