namespace ProfitFox.Bench.MarketData;

/// <summary>
///     The market-data service did not give the history: a run stops without a verdict.
/// </summary>
public sealed class HistoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
