namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The features of the market on the minute a signal fired (decision 1 of add-market-idea-features): a drop of the pair over a day, a drop
///     of BTC over 4 hours and a high funding rate of the pair; their eight combinations are the levels the cases of a bucket split into. A
///     change is a share of the close a span before, <c>close_now / close_before − 1</c>; a number that is not known gives no feature.
/// </summary>
public readonly record struct IdeaFeatures(bool DayDrop, bool BtcDrop, bool HighFunding)
{
    /// <summary>A change of the pair over a day of a day drop, up to which it counts.</summary>
    private const decimal DayDropUpTo = -0.05m;

    /// <summary>A change of BTC over 4 hours of a BTC drop, up to which it counts.</summary>
    private const decimal BtcDropUpTo = -0.01m;

    /// <summary>A funding rate of a high funding, from which it counts.</summary>
    private const decimal HighFundingFrom = 0.0005m;

    public static IdeaFeatures Of(decimal? pairChange, decimal? btcChange, decimal? funding)
    {
        return new IdeaFeatures(pairChange <= DayDropUpTo, btcChange <= BtcDropUpTo, funding >= HighFundingFrom);
    }
}