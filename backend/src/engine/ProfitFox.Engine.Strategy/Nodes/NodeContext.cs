using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     What a node sees on the close of a candle of an instrument besides its inputs.
/// </summary>
/// <param name="InstrumentId">The instrument of the candle.</param>
/// <param name="CloseTime">The close time of the candle.</param>
/// <param name="LastFundingPayment">The last funding payment of the instrument at or before the close; null when there is none.</param>
internal readonly record struct NodeContext(string InstrumentId, DateTimeOffset CloseTime, FundingPayment? LastFundingPayment);