namespace ProfitFox.Presentation.Contracts.Nodes;

/// <summary>
///     A pin of a node kind: its name and type (<c>number</c>, <c>boolean</c>, <c>duration</c> or <c>event</c>).
/// </summary>
public sealed record PinResponse(string Name, string Type);
