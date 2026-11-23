using System.Globalization;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The cycle of an idea minute by minute (change add-ideas-and-market, design.md, decision 5; spec strategy-ideas «Итог
///     идеи»): the rules of a trade of a plan that <see cref="CycleRun.Simulate" /> walks a whole path with, here fed one
///     closed minute at a time as the feed brings it, and the same accounting of <see cref="CycleRun" /> for the outcome.
///     Everything the cycle needs lives in the idea, so after a restart it goes on from <see cref="Idea.ComputedUntil" />.
///     <list type="bullet">
///         <item>entry at the open of the first minute with trades from the moment, worse by the slippage;</item>
///         <item>from the entry minute on, a minute that reaches the stop or the target ends the cycle at its open time;</item>
///         <item>stop and target in one minute: the stop, and the idea is ambiguous;</item>
///         <item>otherwise the cycle ends at the end of its time at the close of its last minute with trades;</item>
///         <item>funding on the payments at <c>entry &lt; t ≤ exit</c>, each at the close of the minute before it.</item>
///     </list>
/// </summary>
public static class IdeaCycle
{
    /// <summary>
    ///     Why an idea is excluded.
    /// </summary>
    public const string NoData = "нет данных";

    public const string Delisted = "пара снята с торгов";

    public const string NoTrade = "нет сделок";

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     A closed minute of the pair of the idea, with trades. Minutes come in the order of their open; one at or before
    ///     <see cref="Idea.ComputedUntil" />, before the moment or from the end of the time on, and any after the exit, change
    ///     nothing. Gives whether the minute ended the cycle.
    /// </summary>
    public static bool Minute(Idea idea, ClosedCandle minute, TradeCosts costs)
    {
        if (idea.State != IdeaState.Open || idea.ExitTime is not null || minute.OpenTime < idea.Moment || minute.OpenTime >= idea.Until
            || minute.OpenTime <= idea.ComputedUntil)
        {
            return false;
        }

        bool isLong = idea.Side == EntrySide.Long;
        if (idea.EntryPrice is null)
        {
            double raw = Price(minute.Open);
            idea.EntryRaw = raw;
            idea.EntryPrice = CycleRun.MarketFill(raw, isLong ? FillSide.Buy : FillSide.Sell, costs);
            idea.EntryTime = minute.OpenTime;
        }

        idea.ComputedUntil = minute.OpenTime;
        idea.LastClose = Price(minute.Close);
        double high = Price(minute.High);
        double low = Price(minute.Low);
        bool stopHit = idea.StopPrice is { } stop && CycleRun.StopHit(isLong, high, low, stop);
        bool targetHit = idea.TargetPrice is { } target && CycleRun.TargetHit(isLong, high, low, target);
        if (!stopHit && !targetHit)
        {
            return false;
        }

        double exitRaw = stopHit ? CycleRun.StopExit(isLong, idea.StopPrice!.Value, Price(minute.Open)) : idea.TargetPrice!.Value;
        idea.ExitTime = minute.OpenTime;
        idea.ExitReason = stopHit ? ExitReason.Stop : ExitReason.Target;
        idea.Ambiguous = stopHit && targetHit;
        idea.ExitRaw = exitRaw;
        return true;
    }

    /// <summary>
    ///     The end of the time of the idea: every minute before it has come (spec market-live-feed «Момент закрытия свечи»).
    ///     Without an exit the cycle ends at the end of its time at the close of its last minute with trades; an idea that had
    ///     no minute with trades is excluded, as a trade of a plan without one is.
    /// </summary>
    public static void TimeUp(Idea idea)
    {
        if (idea.State != IdeaState.Open || idea.ExitTime is not null)
        {
            return;
        }

        if (idea.EntryPrice is null || idea.LastClose is not { } close)
        {
            Exclude(idea, NoTrade);
            return;
        }

        idea.ExitTime = idea.Until;
        idea.ExitReason = ExitReason.Time;
        idea.ExitRaw = close;
    }

    /// <summary>
    ///     A funding payment of the pair of the idea at <paramref name="time" />, priced at <paramref name="price" />, the close
    ///     of the last minute with trades before the minute of the payment. It counts at <c>entry &lt; t ≤ exit</c>.
    /// </summary>
    public static void Payment(Idea idea, DateTimeOffset time, double rate, double price)
    {
        DateTimeOffset end = idea.ExitTime ?? idea.Until;
        if (idea.State != IdeaState.Open || idea.EntryTime is not { } entry || time <= entry || time > end || time <= idea.FundingUntil
            || double.IsNaN(price))
        {
            return;
        }

        idea.FundingPaid += rate * price;
        idea.FundingUntil = time;
    }

    /// <summary>
    ///     Closes an idea whose exit is known with its outcome (spec strategy-ideas «Итог идеи»), counted by
    ///     <see cref="CycleRun" /> with the costs: the result before costs, the fee of both sides, the slippage and the funding.
    /// </summary>
    public static void Close(Idea idea, TradeCosts costs)
    {
        if (idea.State != IdeaState.Open || idea.ExitTime is not { } exit)
        {
            return;
        }

        double raw = idea.ExitRaw!.Value;
        double slippage = idea.ExitReason == ExitReason.Target ? 0 : costs.Slippage;
        double fill = raw * (1 - (idea.Side == EntrySide.Long ? 1 : -1) * slippage);
        CycleOutcome outcome = Outcome(idea, exit, raw, fill, idea.ExitReason, costs);
        idea.State = IdeaState.Closed;
        idea.Result = outcome.Net;
        idea.ResultUsdt = outcome.NetAmount;
    }

    /// <summary>
    ///     The result now (spec strategy-ideas «Результат сейчас»): the idea closed at <paramref name="close" />, the close of
    ///     the last minute of the pair, at <paramref name="at" />, with the costs; null before the entry.
    /// </summary>
    public static double? Now(Idea idea, double close, DateTimeOffset at, TradeCosts costs)
    {
        if (idea.EntryPrice is null || idea.EntryTime is not { } entry)
        {
            return null;
        }

        DateTimeOffset end = at > entry ? at : entry;
        double fill = close * (1 - (idea.Side == EntrySide.Long ? 1 : -1) * costs.Slippage);
        return Outcome(idea, end, close, fill, ExitReason.Time, costs).Net;
    }

    /// <summary>
    ///     Excludes an open idea with its reason (spec strategy-ideas «Исключённая идея»).
    /// </summary>
    public static void Exclude(Idea idea, string reason)
    {
        if (idea.State != IdeaState.Open)
        {
            return;
        }

        idea.State = IdeaState.Excluded;
        idea.Excluded = reason;
    }

    /// <summary>
    ///     A price of a minute as the minute path of a backtest has it: the double its decimal text gives, as the history
    ///     of the service is read.
    /// </summary>
    public static double Price(decimal price)
    {
        return double.Parse(price.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // The cycle of one entry counted by CycleRun. The funding the idea summed goes to it on a path of its own: one payment
    // at the exit, of the sum, priced at 1 the minute before. The cycle reads nothing else of the path, so the path holds
    // only that minute and the minute of the exit (task 18.6): the same numbers in a time that does not grow with the
    // minutes since the entry.
    private static CycleOutcome Outcome(Idea idea, DateTimeOffset exit, double exitRaw, double exitFill, ExitReason reason, TradeCosts costs)
    {
        DateTimeOffset entry = idea.EntryTime!.Value;
        bool later = exit - entry >= OneMinute;
        DateTimeOffset start = later ? exit - OneMinute : entry;
        int length = later ? 2 : 1;
        double[] none = [.. Enumerable.Repeat(double.NaN, length)];
        double[] close = [.. none];
        if (later)
        {
            close[0] = 1;
        }

        var path = new MinutePath(start, none, none, none, close, new bool[length], [(exit, idea.FundingPaid)], entry, exit + OneMinute);
        var cycle = CycleRun.Open(
            path,
            costs,
            idea.Side,
            entry,
            idea.EntryRaw!.Value,
            idea.EntryPrice!.Value,
            (double)idea.Size,
            node: null,
            idea.Stop,
            idea.Target,
            idea.Until);
        cycle.Ambiguous = idea.Ambiguous;
        cycle.Close(exit, exitRaw, exitFill, reason switch
        {
            ExitReason.Stop => CycleExit.Stop,
            ExitReason.Target => CycleExit.Target,
            _ => CycleExit.Time
        });
        return cycle.Outcome();
    }
}
