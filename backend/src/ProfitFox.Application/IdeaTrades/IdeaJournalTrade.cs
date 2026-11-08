using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     A trade of the journal with the name of its account, who took it and the name of the auto that did; an open trade also has the last price of its pair and what it is worth at it in money and
///     in R, none while it waits for its entry or when the exchange gave no price, and the end of the lifetime of its idea while the idea is active.
/// </summary>
public sealed record IdeaJournalTrade(
    IdeaTrade Trade,
    string AccountName,
    decimal? LastPrice = null,
    decimal? CurrentResult = null,
    decimal? CurrentResultR = null,
    IdeaTradeOrigin Origin = IdeaTradeOrigin.Manual,
    string? AutoName = null,
    DateTimeOffset? IdeaEndsAt = null);

/// <summary>Who took a trade: the owner by hand, an auto with money, or an auto on paper.</summary>
public enum IdeaTradeOrigin
{
    Manual,
    Auto,
    Paper
}