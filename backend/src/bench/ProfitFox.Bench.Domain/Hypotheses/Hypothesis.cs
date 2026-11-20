using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Bench.Domain.Hypotheses;

/// <summary>
///     A hypothesis as written in its <c>hypothesis.json</c> (spec hypothesis-bench), checked. <see cref="Hash" /> is the
///     SHA-256 of the file: once the ledger has it, the file is frozen. <see cref="Version" /> numbers the texts of one id from 1.
/// </summary>
public sealed record Hypothesis(
    string Id,
    int Version,
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
