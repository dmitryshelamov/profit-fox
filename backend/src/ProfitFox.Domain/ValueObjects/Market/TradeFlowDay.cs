using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>A day UTC of a pair (its midnight) the daily archive was read for, what it gave and when.</summary>
public sealed record TradeFlowDay(Instrument Instrument, DateTimeOffset Day, TradeFlowDayStatus Status, DateTimeOffset At);