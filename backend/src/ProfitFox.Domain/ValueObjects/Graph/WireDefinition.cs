namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A wire from an output of one node to an input of another.
/// </summary>
public sealed record WireDefinition(PinReference From, PinReference To);