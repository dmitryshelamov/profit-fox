namespace ProfitFox.Engine.Simulation.Trades;

/// <summary>
///     The outcome of a trade plan on the minute path of a pair (spec trade-plan-outcome), by the rules of the Python bench
///     (<c>outcome.py</c>), so the two give the same numbers:
///     <list type="bullet">
///         <item>entry at the open of the first minute with trades from the moment of the event, worse by the slippage;</item>
///         <item>the path runs from the entry minute to the end of the horizon, until the stop or the target;</item>
///         <item>stop and target in one minute: the stop counts, the trade is ambiguous;</item>
///         <item>the stop fills at the worse of its price and the open of the minute, with slippage;</item>
///         <item>the target fills at its price without slippage, also on a gap through it;</item>
///         <item>otherwise the trade closes at the end of the horizon at the close of its last minute with trades;</item>
///         <item>the fee is taken on both sides, funding on the payments at <c>entry &lt; t ≤ exit</c>;</item>
///         <item>coverage of candles and of funding is judged on the whole horizon, not up to the exit.</item>
///     </list>
/// </summary>
public static class TradeSimulator
{
    public static TradeOutcome Simulate(MinutePath path, DateTimeOffset moment, TradePlan plan, TradeCosts costs)
    {
        if (moment.UtcTicks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException("An entry moment is on a minute.", nameof(moment));
        }

        int horizon = plan.HorizonMinutes;
        long first = path.MinuteIndex(moment);
        if (first < 0 || first + horizon > path.Minutes)
        {
            return TradeOutcome.Excluded(TradeStatus.Coverage);
        }

        int start = (int)first;
        int entryStep = -1;
        int lastTraded = -1;
        for (int step = 0; step < horizon; step++)
        {
            if (!path.Covered(start + step))
            {
                return TradeOutcome.Excluded(TradeStatus.Coverage);
            }

            if (!double.IsNaN(path.Open(start + step)))
            {
                entryStep = entryStep < 0 ? step : entryStep;
                lastTraded = step;
            }
        }

        if (entryStep < 0)
        {
            return TradeOutcome.Excluded(TradeStatus.NoTrade);
        }

        if (costs.Funding && path.FundingFrom is { } fundingFrom
                          && !(moment >= fundingFrom && moment + plan.Horizon < path.FundingUntil))
        {
            return TradeOutcome.Excluded(TradeStatus.Funding);
        }

        double side = (int)plan.Side;
        bool isLong = plan.Side == TradeSide.Long;
        double entryRaw = path.Open(start + entryStep);
        double entryFill = entryRaw * (1 + side * costs.Slippage);
        double stopLevel = plan.Stop is { } stop ? entryFill * (1 - side * stop) : double.NaN;
        double targetLevel = plan.Target is { } target ? entryFill * (1 + side * target) : double.NaN;

        int hitStep = -1;
        bool stopHit = false;
        bool targetHit = false;
        for (int step = entryStep; step < horizon && hitStep < 0; step++)
        {
            double high = path.High(start + step);
            double low = path.Low(start + step);
            stopHit = plan.Stop is not null && (isLong ? low <= stopLevel : high >= stopLevel);
            targetHit = plan.Target is not null && (isLong ? high >= targetLevel : low <= targetLevel);
            if (stopHit || targetHit)
            {
                hitStep = step;
            }
        }

        ExitReason reason = stopHit ? ExitReason.Stop : targetHit ? ExitReason.Target : ExitReason.Time;
        double exitRaw;
        switch (reason)
        {
            case ExitReason.Stop:
                double open = path.Open(start + hitStep);
                exitRaw = double.IsNaN(open) ? stopLevel : isLong ? Math.Min(stopLevel, open) : Math.Max(stopLevel, open);
                break;
            case ExitReason.Target:
                exitRaw = targetLevel;
                break;
            default:
                exitRaw = path.Close(start + lastTraded);
                break;
        }

        double exitSlippage = reason == ExitReason.Target ? 0 : costs.Slippage;
        double exitFill = exitRaw * (1 - side * exitSlippage);
        int exitStep = reason == ExitReason.Time ? horizon : hitStep;
        DateTimeOffset entryTime = moment + TimeSpan.FromMinutes(entryStep);
        DateTimeOffset exitTime = moment + TimeSpan.FromMinutes(exitStep);

        double gross = side * (exitRaw / entryRaw - 1);
        double tradedNet = side * (exitFill / entryFill - 1);
        double fees = costs.TakerFee * (1 + exitFill / entryFill);
        double slippage = gross - tradedNet;
        double funding = costs.Funding && path.FundingFrom is not null ? -side * path.FundingPaid(entryTime, exitTime) / entryFill : 0;
        double net = tradedNet - fees + funding;
        double? r = plan.Stop is { } stopFraction ? net / stopFraction : null;

        return new TradeOutcome(
            TradeStatus.Ok,
            reason,
            Ambiguous: stopHit && targetHit,
            entryTime,
            exitTime,
            entryFill,
            exitFill,
            gross,
            fees,
            slippage,
            funding,
            net,
            r);
    }
}
