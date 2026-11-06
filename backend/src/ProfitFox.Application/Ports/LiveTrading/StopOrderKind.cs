namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>What the exchange says an order of its own making is: the target or the stop of an entry.</summary>
public enum StopOrderKind
{
    None,
    TakeProfit,
    StopLoss
}