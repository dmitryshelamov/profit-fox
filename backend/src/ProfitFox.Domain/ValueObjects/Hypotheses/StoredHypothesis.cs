namespace ProfitFox.Domain.ValueObjects.Hypotheses;

/// <summary>
///     A version of a hypothesis as the store keeps it: the text of its <c>hypothesis.json</c> with <c>\n</c> line ends and
///     the hash of that text.
/// </summary>
public sealed record StoredHypothesis(string Id, int Version, string Hash, string Text, DateTimeOffset CreatedAt);
