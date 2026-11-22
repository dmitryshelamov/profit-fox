using System.Globalization;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Presentation.Contracts.Preview.Converters;

/// <summary>
///     The previews of a signal and of a strategy as the API tells them: times in Unix milliseconds UTC, prices as numbers.
/// </summary>
public static class PreviewResponses
{
    public static PreviewResponse Of(SignalPreviewInternal preview)
    {
        return new PreviewResponse(
            preview.Instrument.Symbol,
            Timeframes.Name(preview.Timeframe),
            [.. preview.Candles.Select(Candle)],
            [.. preview.Coverage.Select(range => new PreviewRange(Ms(range.From), Ms(range.Until)))],
            [.. preview.Events.Select(moment => new PreviewEvent(Ms(moment)))],
            Ms(preview.PeriodEnd));
    }

    public static StrategyPreviewResponse Of(StrategyPreviewInternal preview)
    {
        return new StrategyPreviewResponse(
            preview.Instrument.Symbol,
            Timeframes.Name(preview.Timeframe),
            [.. preview.Candles.Select(Candle)],
            [.. preview.Coverage.Select(range => new PreviewRange(Ms(range.From), Ms(range.Until)))],
            [.. preview.Orders.Select(Order)],
            [.. preview.Cycles.Select(Cycle)],
            Ms(preview.PeriodEnd),
            preview.ComputedFrom is { } from ? Ms(from) : null);
    }

    private static PreviewOrder Order(OrderOutcome order)
    {
        string outcome = order.Result switch
        {
            OrderResult.Filled => "filled",
            OrderResult.Skipped => "skipped",
            OrderResult.Excluded => "excluded",
            _ => "not-computed"
        };
        string? reason = order.Exclusion switch
        {
            null => null,
            OrderExclusion.Plan => "plan",
            OrderExclusion.Coverage => "coverage",
            OrderExclusion.NoTrade => "no-trade",
            OrderExclusion.Funding => "funding",
            _ => "period"
        };
        string side = order.Side switch
        {
            OrderSide.Long => "long",
            OrderSide.Short => "short",
            OrderSide.Buy => "buy",
            _ => "sell"
        };
        return new PreviewOrder(Ms(order.Moment), order.Node, side, outcome, reason, order.Price, order.Amount, order.Cycle);
    }

    private static PreviewCycle Cycle(CycleOutcome cycle)
    {
        string? exit = cycle.ExitReason switch
        {
            CycleExit.Stop => "stop",
            CycleExit.Target => "target",
            CycleExit.Time => "time",
            CycleExit.LimitOrder => "limit",
            CycleExit.MarketOrder => "market",
            CycleExit.ClosePosition => "close",
            _ => null
        };
        return new PreviewCycle(
            cycle.Side == EntrySide.Long ? "long" : "short",
            [.. cycle.Fills.Select(Fill)],
            cycle.Entries,
            cycle.MaxInvested,
            exit,
            cycle.Ambiguous,
            cycle.Net,
            cycle.NetAmount,
            cycle.Open,
            cycle.Status == TradeStatus.Ok ? null : "coverage");
    }

    private static PreviewFill Fill(CycleFill fill)
    {
        string action = fill.Action switch
        {
            FillAction.Entry => "entry",
            FillAction.Increase => "increase",
            FillAction.Decrease => "decrease",
            _ => "exit"
        };
        return new PreviewFill(Ms(fill.Moment), action, fill.Side == FillSide.Buy ? "buy" : "sell", fill.Price, fill.Quantity, fill.Amount, fill.Node);
    }

    private static PreviewCandle Candle(HistoryCandle candle)
    {
        return new PreviewCandle(Ms(candle.OpenTime), Number(candle.Open), Number(candle.High), Number(candle.Low), Number(candle.Close), Number(candle.Volume));
    }

    private static double Number(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static long Ms(DateTimeOffset time)
    {
        return time.ToUnixTimeMilliseconds();
    }
}
