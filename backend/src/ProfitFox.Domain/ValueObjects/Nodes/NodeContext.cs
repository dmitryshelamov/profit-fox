using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;

namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     What a node sees at a computation: the last closed candle and the last funding payment paid by its close, the
///     moment of the computation — the close, or the moment of the fill after which the graph is computed again — the
///     state of the position, and whether the computation moves the memory of the nodes (change add-position-node,
///     design.md, decision 7): only the computation at the close of a candle does. <see cref="Pairs" /> are the closed
///     candles of the pairs the graph names (change add-ideas-and-market, design.md, decision 7), by symbol; a pair
///     without a candle of the same open time is not there. <see cref="OpenInterest" /> is the last point of open interest
///     with a time before the close (change add-idea-score, design.md, decision 5), null before the first one.
/// </summary>
internal readonly record struct NodeContext(
    ClosedCandle Candle,
    FundingPayment? LastFunding,
    DateTimeOffset Moment,
    PositionState Position,
    bool Commit,
    IReadOnlyDictionary<string, ClosedCandle>? Pairs = null,
    OpenInterestPoint? OpenInterest = null);
