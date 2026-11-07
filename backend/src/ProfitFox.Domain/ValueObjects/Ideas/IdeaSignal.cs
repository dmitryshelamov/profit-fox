using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A signal of the replay of history: a setup of a pair that fired, with the plan of a type and side and its path (decision 1); the move and
///     the volume of the setup, the version of the rules that made it, and the numbers of the market on the minute it fired: the change of the
///     pair over a day, of BTC over 4 hours and the funding rate of the pair, none where they are not known. It counts as an idea of its key
///     only by an exit rule.
/// </summary>
public sealed record IdeaSignal(
    Instrument Instrument,
    IdeaSetup Setup,
    SignalPath Path,
    decimal Move,
    decimal VolumeRatio,
    int EngineVersion,
    decimal? PairChange = null,
    decimal? BtcChange = null,
    decimal? Funding = null)
{
    public IdeaFeatures Features => IdeaFeatures.Of(PairChange, BtcChange, Funding);

    public IdeaKey Key => new IdeaKey(Instrument, Setup, Path.Type, Path.Interval, Path.Side);

    public IdeaBucketKey Bucket => new IdeaBucketKey(Setup, Path.Type, Path.Interval, Path.Side, Instrument.Category);
}