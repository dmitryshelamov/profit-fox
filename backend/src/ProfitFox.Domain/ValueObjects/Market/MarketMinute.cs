namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>What the metrics need of a closed minute candle: its open time, open, close and turnover in the quote coin.</summary>
public sealed record MarketMinute(DateTimeOffset OpenTime, decimal Open, decimal Close, decimal Turnover);