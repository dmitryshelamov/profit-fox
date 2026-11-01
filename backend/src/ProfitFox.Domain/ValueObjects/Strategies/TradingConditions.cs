using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>What a run trades with: the balance of the bot in the quote asset, the rules of the instrument and the fees.</summary>
public sealed record TradingConditions(decimal Balance, InstrumentRules Rules, FeeSchedule Fees);