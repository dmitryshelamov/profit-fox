using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

internal static class IdeaDbConverter
{
    private static readonly IdeaExitRules DefaultRules = new IdeaExitRules([]);

    internal static IdeaDb FromDomain(Idea idea)
    {
        IdeaTrack track = idea.Track;

        return new IdeaDb
        {
            Id = idea.Id,
            Category = idea.Instrument.Category.ToCode(),
            Symbol = idea.Instrument.Symbol,
            Setup = idea.Setup.ToCode(),
            Type = track.Plan.Type.ToCode(),
            Interval = idea.Interval.ToCode(),
            Side = track.Plan.Side.ToCode(),
            FiredAt = track.FiredAt,
            Entry = track.Plan.Entry,
            Stop = track.Plan.Stop,
            Target = track.Plan.Target,
            RiskReward = track.Plan.RiskReward,
            Status = track.Status.ToCode(),
            StatusAt = track.StatusAt,
            EnteredAt = track.EnteredAt,
            ExitPrice = track.ExitPrice,
            Move = idea.Move,
            VolumeRatio = idea.VolumeRatio,
            PairChange = idea.PairChange,
            BtcChange = idea.BtcChange,
            Funding = idea.Funding,
            EngineVersion = idea.EngineVersion,
            Tick = track.Tick,
            LifeCandles = track.LifeCandles,
            TargetR = track.TargetR,
            EntryCandles = track.EntryCandles
        };
    }

    internal static Idea ToDomain(IdeaDb row)
    {
        MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);
        MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);
        IdeaCodes.TryParseSetup(row.Setup, out IdeaSetup setup);
        IdeaCodes.TryParseType(row.Type, out IdeaType type);
        IdeaCodes.TryParseSide(row.Side, out IdeaSide side);
        var plan = new IdeaPlan(type, side, row.Entry, row.Stop, row.Target);
        // An idea a rolled-back image wrote has no target and window of its own: it lives by the defaults of its type.
        IdeaExitRule defaults = DefaultRules.Of(type, interval);
        var track = new IdeaTrack(
            plan,
            row.FiredAt,
            (int)(interval.CandleEnd(row.FiredAt) - row.FiredAt).TotalMinutes,
            row.LifeCandles,
            row.TargetR ?? defaults.TargetR,
            row.EntryCandles ?? defaults.EntryCandles,
            IdeaCodes.ParseStatus(row.Status),
            row.StatusAt,
            row.EnteredAt,
            row.ExitPrice,
            row.Tick);

        return new Idea(row.Id, new Instrument(category, row.Symbol), setup, interval, track, row.Move, row.VolumeRatio, row.EngineVersion, row.PairChange, row.BtcChange, row.Funding);
    }
}