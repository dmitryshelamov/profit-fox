using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     Where an idea stands: its levels, its status and when it last changed, when it entered and at what price it left. It fired at the end
///     of the minute <see cref="FiredAt" />, on an interval of <see cref="CandleMinutes" /> minutes; its levels go on the price step
///     <see cref="Tick" /> of its pair.
/// </summary>
public sealed record IdeaTrack(
    IdeaPlan Plan,
    DateTimeOffset FiredAt,
    int CandleMinutes,
    IdeaStatus Status,
    DateTimeOffset StatusAt,
    DateTimeOffset? EnteredAt = null,
    decimal? ExitPrice = null,
    decimal? Tick = null)
{
    public static IdeaTrack Fired(IdeaPlan plan, DateTimeOffset firedAt, int candleMinutes, decimal? tick = null)
    {
        return new IdeaTrack(plan, firedAt, candleMinutes, IdeaStatus.AwaitingEntry, firedAt, Tick: tick);
    }
}