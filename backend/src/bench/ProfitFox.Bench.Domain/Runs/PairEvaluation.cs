using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     The events of one block of a pair with their random entries on its minute path (spec hypothesis-bench). Without
///     <c>withEventOutcomes</c> an event gets only its status: its own outcome is never computed.
/// </summary>
public static class PairEvaluation
{
    public static List<EventRow> Evaluate(
        string instrument,
        long block,
        MinutePath path,
        IReadOnlyList<DateTimeOffset> events,
        IReadOnlyList<DateTimeOffset[]> randoms,
        RunDesign design,
        bool withEventOutcomes)
    {
        TradePlan plan = design.Hypothesis.Plan;
        TradeCosts costs = design.Hypothesis.Costs;
        List<EventRow> rows = [];
        for (int index = 0; index < events.Count; index++)
        {
            DateTimeOffset moment = events[index];
            TradeOutcome? outcome = withEventOutcomes ? TradeSimulator.Simulate(path, moment, plan, costs) : null;
            TradeStatus status = outcome?.Status ?? TradeSimulator.Check(path, moment, plan, costs);

            RandomTrade[] trades = [.. randoms[index].Select(entry => new RandomTrade(entry, TradeSimulator.Simulate(path, entry, plan, costs)))];
            TradeFields[] valid = [.. trades.Where(trade => trade.Outcome.Status == TradeStatus.Ok).Select(trade => TradeFields.Of(trade.Outcome, design.Hypothesis.Measure))];
            double? pseudo = valid.Length >= 2 ? valid[0].Y - valid.Skip(1).Average(trade => trade.Y) : null;

            rows.Add(new EventRow(
                instrument,
                moment,
                block,
                status,
                outcome,
                outcome is { Status: TradeStatus.Ok } ? TradeFields.Of(outcome, design.Hypothesis.Measure) : null,
                trades,
                valid.Length,
                TradeFields.Mean(valid),
                pseudo));
        }

        return rows;
    }
}
