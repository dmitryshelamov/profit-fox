using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Fee rates as fractions (<c>0.001</c> is 0.1 %) and the category that decides the asset a fee is charged in.</summary>
public sealed record FeeSchedule(decimal LimitRate, decimal MarketRate, MarketCategory Category);