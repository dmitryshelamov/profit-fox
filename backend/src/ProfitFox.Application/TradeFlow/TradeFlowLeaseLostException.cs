namespace ProfitFox.Application.TradeFlow;

/// <summary>Another collector took the trade flow over; this one stops writing.</summary>
public sealed class TradeFlowLeaseLostException() : Exception("Another collector holds the trade flow now.");