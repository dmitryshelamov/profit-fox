namespace ProfitFox.Application.MarketData;

/// <summary>
///     A read asked for history after the end of the period of the run (spec market-history).
/// </summary>
public sealed class PeriodBoundaryException(string message) : Exception(message);
