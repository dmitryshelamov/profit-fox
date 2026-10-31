namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Carries the value of an output to an input.</summary>
public sealed record Wire(PinRef From, PinRef To);