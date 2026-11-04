namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The minutes with trades of one candle of an interval joined: the first of them opens at <see cref="FirstMinute" />.</summary>
public sealed record MinuteAggregate(DateTimeOffset OpenTime, DateTimeOffset FirstMinute, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);