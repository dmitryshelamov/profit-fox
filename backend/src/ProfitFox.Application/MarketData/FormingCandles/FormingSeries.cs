using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.FormingCandles;

/// <summary>The instrument and interval whose current candle is followed.</summary>
public sealed record FormingSeries(Instrument Instrument, Interval Interval);