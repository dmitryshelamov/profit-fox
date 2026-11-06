using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     Where an idea stands: its levels, its status and when it last changed, when it entered and at what price it left. It fired at the end
///     of the minute <see cref="FiredAt" />, on an interval of <see cref="CandleMinutes" /> minutes; its levels go on the price step
///     <see cref="Tick" /> of its pair. It keeps the exit rule it fired under (decision 6): it lives <see cref="LifeCandles" /> from its entry,
///     a target in R follows the entry by <see cref="TargetR" />, and a pullback or a stop entry waits <see cref="EntryCandles" /> for it.
/// </summary>
public sealed record IdeaTrack(
    IdeaPlan Plan,
    DateTimeOffset FiredAt,
    int CandleMinutes,
    int LifeCandles,
    decimal? TargetR,
    int? EntryCandles,
    IdeaStatus Status,
    DateTimeOffset StatusAt,
    DateTimeOffset? EnteredAt = null,
    decimal? ExitPrice = null,
    decimal? Tick = null)
{
    public static IdeaTrack Fired(IdeaPlan plan, DateTimeOffset firedAt, int candleMinutes, IdeaExitRule rule, decimal? tick = null)
    {
        return new IdeaTrack(plan, firedAt, candleMinutes, rule.LifeCandles, rule.TargetR, rule.EntryCandles, IdeaStatus.AwaitingEntry, firedAt, Tick: tick);
    }
}