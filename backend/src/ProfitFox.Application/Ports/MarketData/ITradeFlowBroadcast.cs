using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.TradeFlow.Live;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     Sends the sums of the current minute and the state of the feed to the viewers of a pair. A send is not awaited by the feed, and its
///     failure is not the feed's: the next update or the snapshot of a new viewer tells the state again.
/// </summary>
public interface ITradeFlowBroadcast
{
    Task MinuteAsync(Instrument instrument, LiveTradeFlow flow);

    Task StatusAsync(Instrument instrument, FormingState state);
}