using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

internal static class SignalDbConverter
{
    internal static SignalDb FromDomain(IdeaSignal signal)
    {
        SignalPath path = signal.Path;

        return new SignalDb
        {
            Category = signal.Instrument.Category.ToCode(),
            Symbol = signal.Instrument.Symbol,
            Setup = signal.Setup.ToCode(),
            Type = path.Type.ToCode(),
            Interval = path.Interval.ToCode(),
            Side = path.Side.ToCode(),
            FiredAt = path.FiredAt,
            EngineVersion = signal.EngineVersion,
            Entry = path.Entry,
            Stop = path.Stop,
            Target = path.Target,
            Move = signal.Move,
            VolumeRatio = signal.VolumeRatio,
            PairChange = signal.PairChange,
            BtcChange = signal.BtcChange,
            Funding = signal.Funding,
            Tick = path.Tick,
            EntryAfter = path.EntryAfter,
            StopBeforeEntryAfter = path.StopBeforeEntryAfter,
            StopAfter = path.StopAfter,
            LadderAfter = [.. path.LadderAfter],
            TargetAfter = path.TargetAfter,
            Closes = [.. path.Closes],
            TrackedUntil = path.TrackedUntil
        };
    }

    internal static (DateTimeOffset FiredAt, IdeaSide Side, Interval Interval) MinuteOf(PairCountDb row)
    {
        IdeaCodes.TryParseSide(row.Side, out IdeaSide side);
        MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);

        return (row.FiredAt, side, interval);
    }

    internal static IdeaSignal ToDomain(SignalDb row)
    {
        MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);
        MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);
        IdeaCodes.TryParseSetup(row.Setup, out IdeaSetup setup);
        IdeaCodes.TryParseType(row.Type, out IdeaType type);
        IdeaCodes.TryParseSide(row.Side, out IdeaSide side);
        var path = new SignalPath(
            type,
            side,
            interval,
            row.FiredAt,
            row.Entry,
            row.Stop,
            row.Target,
            row.Tick,
            row.EntryAfter,
            row.StopBeforeEntryAfter,
            row.StopAfter,
            row.LadderAfter,
            row.TargetAfter,
            row.Closes,
            row.TrackedUntil);

        return new IdeaSignal(new Instrument(category, row.Symbol), setup, path, row.Move, row.VolumeRatio, row.EngineVersion, row.PairChange, row.BtcChange, row.Funding);
    }
}