namespace ProfitFox.Domain.ValueObjects.Hypotheses;

/// <summary>
///     A version of a hypothesis by its id and number: what the runs and the lines of the ledger are of.
/// </summary>
public readonly record struct HypothesisVersion(string Id, int Version);
