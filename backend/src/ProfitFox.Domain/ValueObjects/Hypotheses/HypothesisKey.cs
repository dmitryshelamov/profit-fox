namespace ProfitFox.Domain.ValueObjects.Hypotheses;

/// <summary>
///     A version of a hypothesis and the hash of its text: what a ledger line without a version is placed by.
/// </summary>
public sealed record HypothesisKey(string Id, int Version, string Hash);
