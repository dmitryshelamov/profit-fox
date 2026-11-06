using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     A trade of the journal with the name of its account; an open trade also has the last price of its pair and what it is worth at it in money and
///     in R, none while it waits for its entry or when the exchange gave no price.
/// </summary>
public sealed record IdeaJournalTrade(IdeaTrade Trade, string AccountName, decimal? LastPrice = null, decimal? CurrentResult = null, decimal? CurrentResultR = null);