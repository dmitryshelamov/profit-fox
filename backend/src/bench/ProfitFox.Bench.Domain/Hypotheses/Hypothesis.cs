using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Bench.Domain.Hypotheses;

/// <summary>
///     A hypothesis as written in its <c>hypothesis.json</c> (spec hypothesis-bench), checked. <see cref="Hash" /> is the
///     SHA-256 of the file: once the ledger has it, the file is frozen.
/// </summary>
public sealed record Hypothesis(
    string Folder,
    string Id,
    string Name,
    string Statement,
    DateOnly Created,
    TimeSpan Timeframe,
    TimeBlock Block,
    int RandomsPerEvent,
    Measure Measure,
    double MinEffect,
    TradeCosts Costs,
    Signal Signal,
    TradePlan Plan,
    string Hash);
