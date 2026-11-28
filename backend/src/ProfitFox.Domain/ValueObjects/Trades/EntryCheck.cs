namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     A check before the entry (change take-idea-trades, design.md, decision 7): its code, whether it passed, and the text
///     the entry dialog shows — what holds when it passed, the reason of the refusal when not.
/// </summary>
public sealed record EntryCheck(string Code, bool Passed, string Text);
