namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>How many pairs of the scanner have their history loaded whole, of all its pairs.</summary>
public sealed record MarketHistoryResponse(int Loaded, int Total);