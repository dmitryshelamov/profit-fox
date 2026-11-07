using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The part of the out-of-sample check a signal counts in.</summary>
public enum OutOfSamplePart
{
    Selection,
    Check
}

/// <summary>
///     The window of the out-of-sample check of an exit rule (decisions 1, 7 and 8 of check-idea-exit-rules-out-of-sample): the selection from
///     <see cref="From" />, the check from <see cref="CheckFrom" />, both up to <see cref="To" />, where every rule of the grid has decided every
///     signal fired before it; and the 6 equal blocks of the probability of overfitting over the same span.
/// </summary>
public sealed record OutOfSampleWindow(DateTimeOffset From, DateTimeOffset CheckFrom, DateTimeOffset To)
{
    private const int Blocks = 6;

    private static readonly TimeSpan Days = TimeSpan.FromDays(90);
    private static readonly TimeSpan Checked = TimeSpan.FromDays(30);

    /// <summary>How long after its firing a signal may stay open by the rule: the entry window, a minute at the market, and the lifetime.</summary>
    public static TimeSpan Horizon(IdeaExitRule rule)
    {
        TimeSpan candle = rule.Interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch;
        TimeSpan window = IdeaExitRule.HasWindow(rule.Type) ? candle * rule.EntryCandles!.Value : TimeSpan.FromMinutes(1);

        return window + (candle * rule.LifeCandles);
    }

    /// <summary>The last 90 days and the last 30 of them, up to the horizon of the longest rule of the grid of the candidate before now.</summary>
    public static OutOfSampleWindow Of(IdeaExitRule candidate, DateTimeOffset now)
    {
        TimeSpan longest = Horizon(candidate with { LifeCandles = ExitRuleGrid.Of(candidate.Interval)[^1] });

        return new OutOfSampleWindow(now - Days, now - Checked, now - longest);
    }

    /// <summary>
    ///     The selection up to the <paramref name="horizon" /> of a rule before the check, so no signal of it closes within the check, and the
    ///     check; none for a signal between them or outside the window.
    /// </summary>
    public OutOfSamplePart? PartOf(TimeSpan horizon, DateTimeOffset firedAt)
    {
        if (firedAt < From || firedAt >= To)
        {
            return null;
        }

        if (firedAt >= CheckFrom)
        {
            return OutOfSamplePart.Check;
        }

        return firedAt < CheckFrom - horizon ? OutOfSamplePart.Selection : null;
    }

    /// <summary>
    ///     Which of the 6 equal blocks from <see cref="From" /> to <see cref="To" /> the signal fired in, and whether in its tail, the last
    ///     <paramref name="horizon" /> of it; none outside the window.
    /// </summary>
    public (int Block, bool Tail)? BlockOf(TimeSpan horizon, DateTimeOffset firedAt)
    {
        if (firedAt < From || firedAt >= To)
        {
            return null;
        }

        long span = (To - From).Ticks;
        var block = (int)((firedAt - From).Ticks * Blocks / span);
        DateTimeOffset end = From.AddTicks(span * (block + 1) / Blocks);

        return (block, firedAt >= end - horizon);
    }
}