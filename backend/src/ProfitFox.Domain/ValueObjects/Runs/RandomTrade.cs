using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     A random entry of an event and its outcome.
/// </summary>
public sealed record RandomTrade(DateTimeOffset Entry, TradeOutcome Outcome);
