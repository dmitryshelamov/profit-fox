namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <param name="Path">Node ids from the strategy down to the node, joined by <c>/</c>; empty for the graph itself.</param>
/// <param name="Item">The pin or parameter the error is about, if any.</param>
/// <param name="Reason"></param>
public sealed record GraphError(string Path, string? Item, string Reason);