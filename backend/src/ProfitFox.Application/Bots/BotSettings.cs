using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Bots;

/// <summary>The settings of a bot read from a request, before the checks against its strategy.</summary>
public sealed record BotSettings(string Name, IReadOnlyDictionary<string, NodeValue> ParameterValues, Instrument Instrument, Interval Interval, decimal Balance);