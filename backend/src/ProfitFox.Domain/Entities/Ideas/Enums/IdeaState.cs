namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>
///     The state of an idea (change add-ideas-and-market, design.md, decision 5): open until its plan closes it, closed with
///     its outcome, or excluded — its minutes up to the exit cannot be had.
/// </summary>
public enum IdeaState
{
    Open,
    Closed,
    Excluded
}
