using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The stop of a trade (change take-idea-trades, design.md, decision 8; spec trade-risk «Защитный стоп»): the stop of
///     the idea when it is not farther from the price of the click than the limit of the loss of a trade, and otherwise — and
///     when the idea has none — the price at that limit. The stop of the idea goes to the nearest step of the price; the
///     price at the limit goes to the step toward the price of the click, so the rounding never takes a stop beyond the limit.
/// </summary>
public static class ProtectiveStop
{
    /// <param name="side">The side of the trade.</param>
    /// <param name="price">The price at the click.</param>
    /// <param name="ideaStop">The price of the stop of the idea; null when it has none.</param>
    /// <param name="maxLossPercent">The limit of the loss of a trade, in percent.</param>
    /// <param name="tickSize">The step of the price of the pair; nought for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">The price is not above nought.</exception>
    public static (decimal Price, StopSource Source, decimal LimitPrice) Of(TradeSide side, decimal price, decimal? ideaStop, decimal maxLossPercent, decimal tickSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        int sign = (int)side;
        decimal limit = ToStep(price * (1 - sign * maxLossPercent / 100m), tickSize, sign > 0 ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToNegativeInfinity);
        if (ideaStop is { } stop && (price - stop) * sign > 0 && (price - stop) * sign / price * 100m <= maxLossPercent)
        {
            decimal rounded = ToStep(stop, tickSize, MidpointRounding.AwayFromZero);
            // The nearest step of a stop right at the limit may be beyond it: then the step toward the price.
            if ((price - rounded) * sign / price * 100m > maxLossPercent)
            {
                rounded = ToStep(stop, tickSize, sign > 0 ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToNegativeInfinity);
            }

            return (rounded, StopSource.Idea, limit);
        }

        return (limit, StopSource.Limit, limit);
    }

    /// <summary>
    ///     A price on the step of the pair: the nearest one, or the one up or down.
    /// </summary>
    public static decimal ToStep(decimal value, decimal tickSize, MidpointRounding rounding)
    {
        if (tickSize <= 0)
        {
            return value;
        }

        decimal steps = value / tickSize;
        decimal whole = rounding switch
        {
            MidpointRounding.ToPositiveInfinity => decimal.Ceiling(steps),
            MidpointRounding.ToNegativeInfinity => decimal.Floor(steps),
            _ => decimal.Round(steps, MidpointRounding.AwayFromZero)
        };
        return whole * tickSize;
    }
}
