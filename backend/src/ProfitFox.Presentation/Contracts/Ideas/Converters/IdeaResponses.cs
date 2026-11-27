using ProfitFox.Application.Queries.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Presentation.Contracts.Ideas.Converters;

/// <summary>
///     Ideas as the API sends them.
/// </summary>
public static class IdeaResponses
{
    public static IdeaResponse Of(Idea idea)
    {
        return new IdeaResponse(
            idea.Id,
            idea.Symbol,
            idea.StrategyId,
            idea.StrategyName,
            idea.StrategyVersion,
            Timeframes.Name(idea.Timeframe),
            idea.Side == EntrySide.Long ? "long" : "short",
            idea.Labels,
            idea.Moment.ToUnixTimeMilliseconds(),
            idea.Until.ToUnixTimeMilliseconds(),
            (double)idea.Size,
            idea.ShownEntry,
            idea.EntryTime?.ToUnixTimeMilliseconds(),
            idea.Stop,
            idea.Target,
            idea.StopPrice,
            idea.TargetPrice,
            idea.Now,
            idea.State switch
            {
                IdeaState.Open => "open",
                IdeaState.Closed => "closed",
                _ => "excluded"
            },
            idea.State == IdeaState.Open ? null : idea.ExitTime?.ToUnixTimeMilliseconds(),
            idea.State == IdeaState.Closed
                ? idea.ExitReason switch
                {
                    ExitReason.Stop => "stop",
                    ExitReason.Target => "target",
                    ExitReason.Time => "time",
                    _ => null
                }
                : null,
            idea.Ambiguous,
            idea.Result,
            idea.ResultUsdt,
            idea.Excluded,
            idea.Score);
    }

    public static IdeaPageResponse Of(IdeaListResult result)
    {
        return new IdeaPageResponse([.. result.Page.Items.Select(Of)], result.Page.Total, result.Page.Page, result.Page.PageSize, result.FilterGone);
    }

    public static IdeaSummaryPageResponse Of(IdeaSummaryResult result)
    {
        return new IdeaSummaryPageResponse([.. result.Page.Items.Select(Of)], result.Page.Total, result.Page.Page, result.Page.PageSize, result.FilterGone);
    }

    public static IdeaScoresResponse Of(IdeaScoresResult result)
    {
        return new IdeaScoresResponse([.. result.Rows.Select(row => new IdeaScoreResponse(row.Score, row.Ideas, row.Wins, row.Mean, row.Sum))], result.FilterGone);
    }

    public static IdeaStrategyCountsResponse Of(IdeaStrategiesResult result)
    {
        return new IdeaStrategyCountsResponse(result.Total, [.. result.Strategies.Select(row => new IdeaStrategyCountResponse(row.Strategy, row.Ideas))], result.FilterGone);
    }

    public static ScannerStateResponse Of(ScannerStateInternal state)
    {
        return new ScannerStateResponse(
            state.Worker,
            state.Live,
            state.Idle?.TotalSeconds,
            state.LiveSince?.ToUnixTimeMilliseconds(),
            state.Warmed,
            state.Pairs,
            state.Open,
            state.Closed,
            state.Burst,
            state.Failed,
            state.Failure,
            state.WarmupLeft?.TotalSeconds);
    }

    private static IdeaSummaryResponse Of(IdeaSummary row)
    {
        return new IdeaSummaryResponse(row.StrategyId, row.Strategy, row.Version, row.Timeframe, row.Ideas, row.Wins, row.Mean, row.Sum);
    }
}
