using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>A trade by an idea with the name of the account it trades on.</summary>
public sealed record IdeaTradeView(IdeaTrade Trade, string AccountName);