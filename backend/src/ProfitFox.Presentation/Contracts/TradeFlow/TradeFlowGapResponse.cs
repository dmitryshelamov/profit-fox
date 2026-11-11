namespace ProfitFox.Presentation.Contracts.TradeFlow;

/// <summary>A break of the stream of trades not closed by the archive.</summary>
public sealed record TradeFlowGapResponse(DateTimeOffset From, DateTimeOffset To);