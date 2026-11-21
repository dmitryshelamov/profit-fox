using System.Globalization;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
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
            Ms(preview.PeriodEnd),
            preview.ComputedFrom is { } from ? Ms(from) : null);
    }

    private static PreviewOrder Order(OrderOutcome order)
    {
        string outcome = order.Result switch
        {
            OrderResult.Opened => "opened",
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
        return new PreviewOrder(Ms(order.Moment), order.Side == EntrySide.Long ? "long" : "short", outcome, reason, order.Trade is { } trade ? Trade(trade) : null);
    }

    private static PreviewTrade Trade(TradeOutcome trade)
    {
        string reason = trade.ExitReason switch
        {
            ExitReason.Stop => "stop",
            ExitReason.Target => "target",
            _ => "time"
        };
        return new PreviewTrade(Ms(trade.EntryTime!.Value), trade.EntryPrice!.Value, Ms(trade.ExitTime!.Value), trade.ExitPrice!.Value, reason, trade.Net!.Value);
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
