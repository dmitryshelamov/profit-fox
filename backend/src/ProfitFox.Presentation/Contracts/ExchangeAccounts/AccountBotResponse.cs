namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>A bot that trades with the account.</summary>
public sealed record AccountBotResponse(Guid Id, string Name);