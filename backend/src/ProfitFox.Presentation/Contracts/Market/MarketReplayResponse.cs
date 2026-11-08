using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     How far the replay of ideas has gone: <c>done</c> of the <c>total</c> pairs to replay are replayed up to their coverage; <c>slowest</c> is
///     the earliest day the others have reached, none when none of them has begun; <c>engineVersion</c> the version of the rules it replays.
/// </summary>
public sealed record MarketReplayResponse(int Done, int Total, DateTimeOffset? Slowest, int EngineVersion)
{
    public static MarketReplayResponse Of(IdeaReplayProgress progress)
    {
        return new MarketReplayResponse(progress.Done, progress.Total, progress.Slowest, progress.EngineVersion);
    }
}