namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The symbol of <c>linear</c> on the account as the exchange has it: its leverage, the position (a short below zero) and the number of open orders.</summary>
public sealed record LinearSymbol(decimal Leverage, decimal Position, int OpenOrders);