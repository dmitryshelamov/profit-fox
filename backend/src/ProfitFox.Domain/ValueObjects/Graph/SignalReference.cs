namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     What a <c>signal</c> node of a strategy points at: the id of a saved signal and the number of one of its versions
///     (spec strategy-graph «Узел сигнала»). The id, not the name, so a renamed signal keeps its strategies.
/// </summary>
public readonly record struct SignalReference(Guid Signal, int Version);
