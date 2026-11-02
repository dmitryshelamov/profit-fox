namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>An execution of an order as the exchange reports it; <see cref="FeeCoin" /> is the coin its fee was taken in.</summary>
public sealed record ExchangeFill(string ExecId, string ClientId, decimal Quantity, decimal Price, decimal Fee, string FeeCoin, DateTimeOffset At);