using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

/// <summary>
///     An idea and its row: the side, the state and the reason of the exit as words, the timeframe by its name, the time of
///     the plan in minutes; the prices of the stop and the target are counted from the entry for the lists to sort by.
/// </summary>
internal static class IdeaDbConverter
{
    public static Idea ToDomain(IdeaDb row)
    {
        return new Idea
        {
            Id = row.Id,
            StrategyId = row.StrategyId,
            StrategyName = row.StrategyName,
            StrategyVersion = row.StrategyVersion,
            Timeframe = Timeframes.TryParse(row.Timeframe, out TimeSpan timeframe) ? timeframe : throw new InvalidOperationException($"{row.Timeframe} is not a timeframe."),
            Symbol = row.Symbol,
            Side = row.Side == "long" ? EntrySide.Long : EntrySide.Short,
            Moment = row.Moment,
            Size = row.Size,
            After = TimeSpan.FromMinutes(row.AfterMinutes),
            Stop = row.Stop,
            Target = row.Target,
            CandleClose = row.CandleClose,
            Labels = row.Labels,
            State = State(row.State),
            EntryRaw = row.EntryRaw,
            EntryPrice = row.EntryPrice,
            EntryTime = row.EntryTime,
            FundingPaid = row.FundingPaid,
            FundingUntil = row.FundingUntil,
            LastClose = row.LastClose,
            ComputedUntil = row.ComputedUntil,
            Now = row.NowResult,
            NowAt = row.NowAt,
            ExitTime = row.ExitTime,
            ExitReason = row.ExitReason switch
            {
                "stop" => ExitReason.Stop,
                "target" => ExitReason.Target,
                "time" => ExitReason.Time,
                _ => ExitReason.None
            },
            Ambiguous = row.Ambiguous,
            ExitRaw = row.ExitPrice,
            Result = row.Result,
            ResultUsdt = row.ResultUsdt,
            Excluded = row.Excluded
        };
    }

    /// <summary>
    ///     The values of the statements that write an idea.
    /// </summary>
    public static object Parameters(Idea idea)
    {
        return new
        {
            idea.Id,
            idea.StrategyId,
            idea.StrategyName,
            idea.StrategyVersion,
            Timeframe = Timeframes.Name(idea.Timeframe),
            idea.Symbol,
            Side = idea.Side == EntrySide.Long ? "long" : "short",
            idea.Moment,
            idea.Size,
            AfterMinutes = (int)idea.After.TotalMinutes,
            idea.Until,
            idea.Stop,
            idea.Target,
            idea.CandleClose,
            Labels = idea.Labels.ToArray(),
            State = State(idea.State),
            idea.EntryRaw,
            idea.EntryPrice,
            idea.EntryTime,
            idea.StopPrice,
            idea.TargetPrice,
            idea.FundingPaid,
            idea.FundingUntil,
            idea.LastClose,
            idea.ComputedUntil,
            NowResult = idea.Now,
            idea.NowAt,
            idea.ExitTime,
            ExitReason = idea.ExitReason switch
            {
                ExitReason.Stop => "stop",
                ExitReason.Target => "target",
                ExitReason.Time => "time",
                _ => null
            },
            idea.Ambiguous,
            ExitPrice = idea.ExitRaw,
            idea.Result,
            idea.ResultUsdt,
            idea.Excluded
        };
    }

    public static string State(IdeaState state)
    {
        return state switch
        {
            IdeaState.Open => "open",
            IdeaState.Closed => "closed",
            _ => "excluded"
        };
    }

    private static IdeaState State(string state)
    {
        return state switch
        {
            "open" => IdeaState.Open,
            "closed" => IdeaState.Closed,
            _ => IdeaState.Excluded
        };
    }
}
