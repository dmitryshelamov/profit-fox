namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The orders that gave no trade, by reason: no plan, a hole in the minutes, a hole in funding, no trade in the minutes,
///     a time that ends after the end of history.
/// </summary>
public sealed record ExclusionsResponse(int Plan, int Coverage, int Funding, int NoTrade, int Period);
