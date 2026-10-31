namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Position of a node on the canvas; never affects validation or execution.</summary>
public sealed record NodePosition(decimal X, decimal Y);