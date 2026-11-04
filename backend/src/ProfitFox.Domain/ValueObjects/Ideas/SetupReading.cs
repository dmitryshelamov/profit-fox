namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The setups of a pair on an interval on the last closed minute, and the ATR they are measured in.</summary>
public sealed record SetupReading(decimal Atr, IReadOnlyList<SetupSignal> Signals);