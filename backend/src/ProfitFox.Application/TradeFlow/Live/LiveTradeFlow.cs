using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.TradeFlow.Live;

/// <summary>The sums of the current minute of a pair since <see cref="Since" />, the moment its feed subscribed; only for showing, never stored.</summary>
public sealed record LiveTradeFlow(TradeFlowMinute Minute, DateTimeOffset Since);