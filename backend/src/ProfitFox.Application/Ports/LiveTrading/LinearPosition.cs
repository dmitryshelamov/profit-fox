namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>The symbol of <c>linear</c> on the account as one read of the exchange tells it: its leverage, with a position or without, and the position, a short below zero.</summary>
public sealed record LinearPosition(decimal Leverage, decimal Size);