using ProfitFox.Domain.Execution;
using ProfitFox.Domain.MarketData;

namespace ProfitFox.Domain.Signals;

/// <summary>
///     The candles of one pair as a graph gets them, a signal and a strategy alike (spec signal-preview): only the candles
///     with trades, in the order given, each with the last funding payment paid by its close.
/// </summary>
public static class PairCandles
{
    public static IEnumerable<(ClosedCandle Candle, FundingPayment? LastFunding)> Closed(
        TimeSpan timeframe,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding)
    {
        FundingPayment[] payments = [.. funding.Select(payment => payment.ToFundingPayment()).OrderBy(payment => payment.Time)];
        int paid = 0;
        foreach (HistoryCandle candle in candles.Where(candle => candle.HasTrades))
        {
            var closed = candle.ToClosedCandle(timeframe);
            while (paid < payments.Length && payments[paid].Time <= closed.CloseTime)
            {
                paid++;
            }

            yield return (closed, paid > 0 ? payments[paid - 1] : null);
        }
    }
}
