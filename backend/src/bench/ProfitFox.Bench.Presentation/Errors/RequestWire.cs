namespace ProfitFox.Bench.Presentation.Errors;

/// <summary>
///     A wire of a graph as written in it: <c>node.pin</c> to <c>node.pin</c>.
/// </summary>
public sealed record RequestWire(string From, string To);
