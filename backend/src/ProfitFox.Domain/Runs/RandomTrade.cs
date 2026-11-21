using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Domain.Runs;

/// <summary>
///     A random entry of an event and its outcome.
/// </summary>
public sealed record RandomTrade(DateTimeOffset Entry, TradeOutcome Outcome);
