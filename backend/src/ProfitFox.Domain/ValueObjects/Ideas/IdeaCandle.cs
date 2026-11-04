namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>A candle the ideas are computed on: a minute or a candle of an interval; the volume is the turnover in the quote coin.</summary>
public sealed record IdeaCandle(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);