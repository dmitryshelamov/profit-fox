using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

/// <summary>An idea of a market long of 15 minutes on <c>linear</c> / <c>WIFUSDT</c> unless told otherwise: entry 105, stop 100, target 115.</summary>
public static class IdeaGenerator
{
    public static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");

    public static Idea Generate(
        DateTimeOffset firedAt,
        IdeaStatus status = IdeaStatus.AwaitingEntry,
        Instrument? instrument = null,
        IdeaSetup setup = IdeaSetup.Impulse,
        IdeaType type = IdeaType.TwoR,
        IdeaSide side = IdeaSide.Long,
        Interval interval = Interval.FifteenMinutes,
        DateTimeOffset? statusAt = null,
        int engineVersion = IdeasEngineVersion.Current,
        decimal? tick = null)
    {
        bool entered = status is not IdeaStatus.AwaitingEntry;
        decimal? exit = status is IdeaStatus.Target ? 115m : status is IdeaStatus.Stop ? 100m : status is IdeaStatus.Expired ? 106m : null;
        var track = new IdeaTrack(
            new IdeaPlan(type, side, 105m, side == IdeaSide.Long ? 100m : 110m, side == IdeaSide.Long ? 115m : 95m),
            firedAt,
            (int)(interval.CandleEnd(firedAt) - firedAt).TotalMinutes,
            status,
            statusAt ?? firedAt,
            entered ? firedAt : null,
            exit,
            tick);

        return new Idea(Guid.NewGuid(), instrument ?? Wif, setup, interval, track, 3.5m, 2.25m, engineVersion);
    }
}