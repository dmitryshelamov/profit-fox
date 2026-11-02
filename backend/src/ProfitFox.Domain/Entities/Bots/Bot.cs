using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Bots;

/// <summary>
///     A strategy set up to trade. <see cref="ParameterValues" /> holds only the values the owner gave; the other parameters take
///     the default of the latest strategy version. <see cref="Balance" /> is in the quote asset. <see cref="ExchangeAccountId" /> is the
///     account the bot will trade with, <c>null</c> for a bot that only runs backtests.
/// </summary>
public sealed record Bot(
    Guid Id,
    string Name,
    Guid StrategyId,
    IReadOnlyDictionary<string, NodeValue> ParameterValues,
    Instrument Instrument,
    Interval Interval,
    decimal Balance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ExchangeAccountId = null);