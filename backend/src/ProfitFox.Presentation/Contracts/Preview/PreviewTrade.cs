namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     The trade an order opened: entry and exit in Unix milliseconds UTC with their prices, the reason of the exit —
///     <c>stop</c>, <c>target</c> or <c>time</c> — and the result after costs as a fraction of the amount.
/// </summary>
public sealed record PreviewTrade(long EntryTime, double EntryPrice, long ExitTime, double ExitPrice, string ExitReason, double Net);
