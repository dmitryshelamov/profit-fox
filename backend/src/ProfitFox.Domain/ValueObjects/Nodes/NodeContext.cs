using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Positions;

namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     What a node sees at a computation: the last closed candle and the last funding payment paid by its close, the
///     moment of the computation — the close, or the moment of the fill after which the graph is computed again — the
///     state of the position, and whether the computation moves the memory of the nodes (change add-position-node,
///     design.md, decision 7): only the computation at the close of a candle does.
/// </summary>
internal readonly record struct NodeContext(ClosedCandle Candle, FundingPayment? LastFunding, DateTimeOffset Moment, PositionState Position, bool Commit);
