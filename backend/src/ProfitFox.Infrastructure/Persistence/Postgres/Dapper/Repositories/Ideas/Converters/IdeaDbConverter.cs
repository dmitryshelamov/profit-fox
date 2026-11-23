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
            ExitReason = Reason(idea.ExitReason),
            idea.Ambiguous,
            ExitPrice = idea.ExitRaw,
            idea.Result,
            idea.ResultUsdt,
            idea.Excluded
        };
    }

    /// <summary>
    ///     What changed in many ideas as arrays of one parameter each, in the order of the ideas, for one statement over
    ///     <c>unnest</c> (task 18.6).
    /// </summary>
    public static object Batch(IReadOnlyCollection<Idea> ideas)
    {
        return new
        {
            Ids = ideas.Select(idea => idea.Id).ToArray(),
            States = ideas.Select(idea => State(idea.State)).ToArray(),
            EntryRaws = ideas.Select(idea => idea.EntryRaw).ToArray(),
            EntryPrices = ideas.Select(idea => idea.EntryPrice).ToArray(),
            EntryTimes = ideas.Select(idea => idea.EntryTime).ToArray(),
            StopPrices = ideas.Select(idea => idea.StopPrice).ToArray(),
            TargetPrices = ideas.Select(idea => idea.TargetPrice).ToArray(),
            FundingPaids = ideas.Select(idea => idea.FundingPaid).ToArray(),
            FundingUntils = ideas.Select(idea => idea.FundingUntil).ToArray(),
            LastCloses = ideas.Select(idea => idea.LastClose).ToArray(),
            ComputedUntils = ideas.Select(idea => idea.ComputedUntil).ToArray(),
            NowResults = ideas.Select(idea => idea.Now).ToArray(),
            NowAts = ideas.Select(idea => idea.NowAt).ToArray(),
            ExitTimes = ideas.Select(idea => idea.ExitTime).ToArray(),
            ExitReasons = ideas.Select(idea => Reason(idea.ExitReason)).ToArray(),
            Ambiguous = ideas.Select(idea => idea.Ambiguous).ToArray(),
            ExitPrices = ideas.Select(idea => idea.ExitRaw).ToArray(),
            Results = ideas.Select(idea => idea.Result).ToArray(),
            ResultUsdts = ideas.Select(idea => idea.ResultUsdt).ToArray(),
            Excludeds = ideas.Select(idea => idea.Excluded).ToArray()
        };
    }

    private static string? Reason(ExitReason reason)
    {
        return reason switch
        {
            ExitReason.Stop => "stop",
            ExitReason.Target => "target",
            ExitReason.Time => "time",
            _ => null
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
