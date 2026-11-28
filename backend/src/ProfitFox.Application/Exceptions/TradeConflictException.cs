namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A request about a trade or the risk module that its state does not allow now (change take-idea-trades, design.md,
///     decision 13), with the reason; the API answers 409. Nothing was written.
/// </summary>
public sealed class TradeConflictException(string reason) : Exception(reason);
