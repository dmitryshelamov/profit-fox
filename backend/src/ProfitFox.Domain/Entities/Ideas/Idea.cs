using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.Ideas;

/// <summary>
///     A trade idea: a setup of a pair on an interval that fired, with the levels of its type and side and where it stands. It keeps the move
///     and the volume of the setup it fired on, and the version of the rules that made it. Only advice: nothing trades on it.
/// </summary>
public sealed record Idea(
    Guid Id,
    Instrument Instrument,
    IdeaSetup Setup,
    Interval Interval,
    IdeaTrack Track,
    decimal Move,
    decimal VolumeRatio,
    int EngineVersion)
{
    public IdeaKey Key => new IdeaKey(Instrument, Setup, Track.Plan.Type, Interval, Track.Plan.Side);

    public bool IsOpen => Track.Status is IdeaStatus.AwaitingEntry or IdeaStatus.Active;
}