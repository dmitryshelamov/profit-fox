using ProfitFox.Domain.Execution;

namespace ProfitFox.Domain.Nodes;

/// <summary>
///     What a node sees at a candle: the candle and the last funding payment paid by its close.
/// </summary>
internal readonly record struct NodeContext(ClosedCandle Candle, FundingPayment? LastFunding);
