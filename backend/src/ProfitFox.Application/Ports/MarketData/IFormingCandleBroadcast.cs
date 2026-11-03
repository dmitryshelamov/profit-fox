using ProfitFox.Application.MarketData.FormingCandles;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Sends the current candle and the state of its feed to the viewers of a series. A send is not awaited by the feed, and its
///     failure is not the feed's: the next update or the snapshot of a new viewer tells the state again.
/// </summary>
public interface IFormingCandleBroadcast
{
    Task CandleAsync(FormingSeries series, FormingCandle candle);

    Task StatusAsync(FormingSeries series, FormingState state);
}