using System.Globalization;

using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy;

/// <summary>
///     Feeds an executor as the bench will: candles grouped by close time in time order, and before each moment every funding payment at
///     or before its close.
/// </summary>
internal static class Feed
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    /// <summary>
    ///     The results of every close, in order.
    /// </summary>
    public static List<CloseResult> Run(CompositeNode graph, IEnumerable<ClosedCandle> candles, IEnumerable<FundingPayment> payments)
    {
        var executor = new GraphExecutor(graph);
        var pending = new Queue<FundingPayment>(payments.OrderBy(payment => payment.Time));
        List<CloseResult> results = [];
        foreach (IGrouping<DateTimeOffset, ClosedCandle> moment in candles.GroupBy(candle => candle.CloseTime).OrderBy(moment => moment.Key))
        {
            while (pending.TryPeek(out FundingPayment? payment) && payment.Time <= moment.Key)
            {
                executor.AddFundingPayment(pending.Dequeue());
            }

            results.AddRange(executor.Close(moment.Key, moment.ToList()));
        }

        return results;
    }

    /// <summary>
    ///     A UTC moment written as <c>2026-01-01T04:00</c>.
    /// </summary>
    public static DateTimeOffset At(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    ///     A 4h candle of the instrument closing at <paramref name="close" />.
    /// </summary>
    public static ClosedCandle Candle(string instrument, string close)
    {
        DateTimeOffset closeTime = At(close);
        return new ClosedCandle(instrument, closeTime - FourHours, closeTime);
    }

    /// <summary>
    ///     A funding payment of the instrument.
    /// </summary>
    public static FundingPayment Payment(string instrument, string time, decimal rate)
    {
        return new FundingPayment(instrument, At(time), rate);
    }

    /// <summary>
    ///     The number on the output <c>value</c> of each result.
    /// </summary>
    public static decimal?[] Numbers(IEnumerable<CloseResult> results)
    {
        return results.Select(result => result.Outputs["value"].Number).ToArray();
    }
}