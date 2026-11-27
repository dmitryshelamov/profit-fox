using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Execution;

/// <summary>
///     What a graph knows of a pair at the close of a candle besides the candle (change add-idea-score, design.md, decision
///     5): the last funding payment and the last point of open interest with a time before the close, each null before the
///     first one. A payment alone converts to facts without open interest, as a graph without the node «OI» gets them.
/// </summary>
public readonly record struct CandleFacts(FundingPayment? Funding, OpenInterestPoint? OpenInterest = null)
{
    public static implicit operator CandleFacts(FundingPayment? funding)
    {
        return new CandleFacts(funding);
    }
}
