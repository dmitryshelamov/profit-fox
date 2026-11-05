using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     Steps an idea by the next closed minute. A market idea enters at the open of the minute after it fired; a pullback waits up to 4
///     candles for a minute that touches its entry. An active idea that reaches its stop is «Стоп», its target «Цель», both in one minute
///     «Стоп»; one that reaches neither in 24 candles from its entry expires at the close of its last minute.
/// </summary>
public static class IdeaTracker
{
    private const int EntryCandles = 4;
    private const int LifeCandles = 24;

    public static IdeaTrack Step(IdeaTrack track, IdeaCandle minute)
    {
        return track.Status switch
        {
            IdeaStatus.AwaitingEntry when track.Plan.Type != IdeaType.Pullback => EnterAtMarket(track, minute),
            IdeaStatus.AwaitingEntry => EnterAtLimit(track, minute),
            IdeaStatus.Active => Exit(track, minute),
            _ => track
        };
    }

    /// <summary>Closes an open idea of a pair that is out of trading: «Истекла» where it stands, an active one at the last close known.</summary>
    public static IdeaTrack Expire(IdeaTrack track, DateTimeOffset at, decimal? close)
    {
        return track with { Status = IdeaStatus.Expired, StatusAt = at, ExitPrice = track.EnteredAt is null ? null : close };
    }

    private static IdeaTrack EnterAtMarket(IdeaTrack track, IdeaCandle minute)
    {
        IdeaPlan plan = track.Plan.Type == IdeaType.Levels ? track.Plan with { Entry = minute.Open } : TwoR(track.Plan, minute.Open);

        // Beyond its stop an idea has no risk to measure, beyond its target nothing to gain.
        if (Ahead(plan) * (minute.Open - plan.Stop) <= 0 || Ahead(plan) * (plan.Target - minute.Open) <= 0)
        {
            return track with { Status = IdeaStatus.Expired, StatusAt = minute.OpenTime };
        }

        return Exit(track with { Plan = plan, Status = IdeaStatus.Active, StatusAt = minute.OpenTime, EnteredAt = minute.OpenTime }, minute);
    }

    private static IdeaTrack EnterAtLimit(IdeaTrack track, IdeaCandle minute)
    {
        if (minute.OpenTime >= track.FiredAt.AddMinutes(EntryCandles * track.CandleMinutes))
        {
            return track with { Status = IdeaStatus.Expired, StatusAt = minute.OpenTime };
        }

        IdeaPlan plan = track.Plan;

        if (Ahead(plan) * (Worst(plan, minute) - plan.Entry) > 0)
        {
            return track;
        }

        IdeaTrack entered = track with { Status = IdeaStatus.Active, StatusAt = minute.OpenTime, EnteredAt = minute.OpenTime };

        // Within the minute of the fill the target may have come before it; only the stop counts.
        return Ahead(plan) * (Worst(plan, minute) - plan.Stop) <= 0 ? entered with { Status = IdeaStatus.Stop, ExitPrice = plan.Stop } : entered;
    }

    private static IdeaTrack Exit(IdeaTrack track, IdeaCandle minute)
    {
        IdeaPlan plan = track.Plan;

        if (Ahead(plan) * (Worst(plan, minute) - plan.Stop) <= 0)
        {
            return track with { Status = IdeaStatus.Stop, StatusAt = minute.OpenTime, ExitPrice = plan.Stop };
        }

        if (Ahead(plan) * (Best(plan, minute) - plan.Target) >= 0)
        {
            return track with { Status = IdeaStatus.Target, StatusAt = minute.OpenTime, ExitPrice = plan.Target };
        }

        DateTimeOffset end = minute.OpenTime.AddMinutes(1);

        return end >= track.EnteredAt!.Value.AddMinutes(LifeCandles * track.CandleMinutes)
            ? track with { Status = IdeaStatus.Expired, StatusAt = end, ExitPrice = minute.Close }
            : track;
    }

    private static IdeaPlan TwoR(IdeaPlan plan, decimal entry)
    {
        return plan with { Entry = entry, Target = entry + (2m * (entry - plan.Stop)) };
    }

    private static decimal Ahead(IdeaPlan plan)
    {
        return plan.Side == IdeaSide.Long ? 1m : -1m;
    }

    // The price of a minute farthest against the idea, and the one farthest for it.
    private static decimal Worst(IdeaPlan plan, IdeaCandle minute)
    {
        return plan.Side == IdeaSide.Long ? minute.Low : minute.High;
    }

    private static decimal Best(IdeaPlan plan, IdeaCandle minute)
    {
        return plan.Side == IdeaSide.Long ? minute.High : minute.Low;
    }
}