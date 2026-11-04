using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Market.Queries.GetMarket;

/// <summary>The rows of the snapshot and the state of the scanner; a pause of REST that has ended is none.</summary>
public sealed record GetMarketResult(ScannerState State, IReadOnlyList<MarketSnapshotRow> Rows);