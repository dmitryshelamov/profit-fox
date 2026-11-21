using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Graph;

namespace ProfitFox.Domain.Hypotheses;

/// <summary>
///     A hypothesis as written in its <c>hypothesis.json</c> (spec hypothesis-bench), checked. <see cref="Hash" /> is the
///     SHA-256 of the file: once the ledger has it, the file is frozen. <see cref="Version" /> numbers the texts of one id from 1.
///     <see cref="Strategy" /> is what a run executes: the strategy put together from <see cref="Signal" /> and
///     <see cref="Plan" /> when the text is read; the text and its hash do not know of it.
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
    TradingStrategy Strategy,
    string Hash);
