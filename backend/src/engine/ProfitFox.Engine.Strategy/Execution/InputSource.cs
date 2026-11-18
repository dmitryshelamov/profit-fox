namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     Where an input takes its value: a slot written by another node, or a written value when <see cref="Slot" /> is -1.
/// </summary>
internal readonly record struct InputSource(int Slot, PinValue Value);
