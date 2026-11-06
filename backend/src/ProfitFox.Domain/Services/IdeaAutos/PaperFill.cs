using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     The fill of a paper trade by the closed minutes of its pair. A pullback waits for a minute that touches its limit before the deadline of
///     its entry and enters at the limit; the other types enter at the close of the minute the idea was born in. An open trade leaves at the stop or
///     at the target of the first minute that touches one, at the stop when a minute touches both, and has no lifetime. In the minute of the entry
///     of a pullback only the stop counts. The entry pays the maker fee at a limit and the taker fee otherwise, the exit the taker fee.
/// </summary>
public static class PaperFill
{
    /// <summary>The price a paper trade of the idea enters at: its limit for a pullback, otherwise the close of the minute the idea was born in.</summary>
    public static decimal EntryPrice(Idea idea, IdeaCandle birth)
    {
        return idea.Track.Plan.Type == IdeaType.Pullback ? idea.Track.Plan.Entry : birth.Close;
    }

    /// <param name="id"></param>

    /// <param name="idea"></param>
    /// <param name="autoId"></param>
    /// <param name="accountId"></param>    /// <param name="size">The size at <see cref="EntryPrice" />.</param>
    /// <param name="promise"></param>
    /// <param name="birth">The minute the idea was born in; the trade is made at its end and stepped from the next minute on.</param>
    /// <param name="fees"></param>
    public static PaperIdeaTrade Open(Guid id, Idea idea, Guid autoId, Guid accountId, IdeaTradeSize size, IdeaPromise? promise, IdeaCandle birth, FeeRates fees)
    {
        DateTimeOffset now = birth.OpenTime.AddMinutes(1);
        decimal price = EntryPrice(idea, birth);
        var trade = IdeaTrade.Create(id, idea, accountId, price, size, now, promise, autoId);

        if (idea.Track.Plan.Type != IdeaType.Pullback)
        {
            trade = trade.EntryGrown(Leg(size.Quantity, price, fees.MarketPercent), now);
        }

        return new PaperIdeaTrade(trade, autoId, now);
    }

    /// <summary>Steps the trade by a closed minute of its pair; a minute it has stepped already, and a trade that is done, change nothing.</summary>
    public static PaperIdeaTrade Step(PaperIdeaTrade paper, IdeaCandle minute, FeeRates fees)
    {
        if (minute.OpenTime < paper.SteppedUntil || !paper.Trade.IsOpen)
        {
            return paper;
        }

        IdeaTrade stepped = paper.Trade.Status == IdeaTradeStatus.AwaitingEntry ? Enter(paper.Trade, minute, fees) : Exit(paper.Trade, minute, fees);

        return paper with { Trade = stepped, SteppedUntil = minute.OpenTime.AddMinutes(1) };
    }

    private static IdeaTrade Enter(IdeaTrade trade, IdeaCandle minute, FeeRates fees)
    {
        if (trade.EntryExpired(minute.OpenTime))
        {
            return trade.EntryEnded(minute.OpenTime);
        }

        if (Ahead(trade) * (Worst(trade, minute) - trade.EntryPrice) > 0)
        {
            return trade;
        }

        IdeaTrade entered = trade.EntryGrown(Leg(trade.Quantity, trade.EntryPrice, fees.LimitPercent), minute.OpenTime);

        // Within the minute of the fill the target may have come before it: only the stop counts.
        return StopTouched(entered, minute) ? Leave(entered, IdeaTradeLeg.StopLoss, entered.Stop, minute, fees) : entered;
    }

    private static IdeaTrade Exit(IdeaTrade trade, IdeaCandle minute, FeeRates fees)
    {
        if (StopTouched(trade, minute))
        {
            return Leave(trade, IdeaTradeLeg.StopLoss, trade.Stop, minute, fees);
        }

        return Ahead(trade) * (Best(trade, minute) - trade.Target) >= 0 ? Leave(trade, IdeaTradeLeg.TakeProfit, trade.Target, minute, fees) : trade;
    }

    private static IdeaTrade Leave(IdeaTrade trade, IdeaTradeLeg leg, decimal price, IdeaCandle minute, FeeRates fees)
    {
        return trade.ExitGrown(leg, Leg(trade.HeldQuantity, price, fees.MarketPercent), minute.OpenTime);
    }

    private static bool StopTouched(IdeaTrade trade, IdeaCandle minute)
    {
        return Ahead(trade) * (Worst(trade, minute) - trade.Stop) <= 0;
    }

    private static LegTotals Leg(decimal quantity, decimal price, decimal feePercent)
    {
        decimal value = quantity * price;

        return new LegTotals(quantity, value, value * feePercent / 100m);
    }

    private static decimal Ahead(IdeaTrade trade)
    {
        return trade.Side == IdeaSide.Long ? 1m : -1m;
    }

    // The price of a minute farthest against the trade, and the one farthest for it.
    private static decimal Worst(IdeaTrade trade, IdeaCandle minute)
    {
        return trade.Side == IdeaSide.Long ? minute.Low : minute.High;
    }

    private static decimal Best(IdeaTrade trade, IdeaCandle minute)
    {
        return trade.Side == IdeaSide.Long ? minute.High : minute.Low;
    }
}