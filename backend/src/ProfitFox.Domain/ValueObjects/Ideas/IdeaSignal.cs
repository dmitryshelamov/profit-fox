using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A signal of the replay of history: a setup of a pair that fired, with the plan of a type and side and its path (decision 1); the move and
///     the volume of the setup, and the version of the rules that made it. It counts as an idea of its key only by an exit rule.
/// </summary>
public sealed record IdeaSignal(Instrument Instrument, IdeaSetup Setup, SignalPath Path, decimal Move, decimal VolumeRatio, int EngineVersion)
{
    public IdeaKey Key => new IdeaKey(Instrument, Setup, Path.Type, Path.Interval, Path.Side);

    public IdeaBucketKey Bucket => new IdeaBucketKey(Setup, Path.Type, Path.Interval, Path.Side, Instrument.Category);
}