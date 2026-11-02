using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Bots;

/// <summary>The exchange account a bot trades with, as far as the checks of the bot need it: the categories its key trades in.</summary>
public sealed record BotAccount(Guid Id, IReadOnlySet<MarketCategory> Categories);