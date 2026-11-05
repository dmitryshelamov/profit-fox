using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarket;

/// <summary>The rows of the snapshot with the setups of the pairs that have some, and the state of the scanner; a pause of REST that has ended is none.</summary>
public sealed record GetMarketResult(ScannerState State, IReadOnlyList<MarketSnapshotRow> Rows, IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> Setups);