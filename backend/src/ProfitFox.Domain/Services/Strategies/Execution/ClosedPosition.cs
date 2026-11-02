namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>A position an event closed: when, and what it realized with its fees in the quote asset.</summary>
public sealed record ClosedPosition(int Number, DateTimeOffset At, decimal RealizedPnl, decimal Fees);