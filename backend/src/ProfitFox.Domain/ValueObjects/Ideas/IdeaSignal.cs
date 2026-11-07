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
    /// <summary>
    ///     Its features with the count of the distinct pairs of its side on its interval in its minute, which a signal does not keep: the pass of
    ///     the statistics counts it from the signals (decision 2 of add-burst-and-auto-limits).
    /// </summary>
    public IdeaFeatures FeaturesWith(int? burstPairs)
    {
        return IdeaFeatures.Of(PairChange, BtcChange, Funding, burstPairs, Path.Interval);
    }

    public IdeaKey Key => new IdeaKey(Instrument, Setup, Path.Type, Path.Interval, Path.Side);

    public IdeaBucketKey Bucket => new IdeaBucketKey(Setup, Path.Type, Path.Interval, Path.Side, Instrument.Category);
}