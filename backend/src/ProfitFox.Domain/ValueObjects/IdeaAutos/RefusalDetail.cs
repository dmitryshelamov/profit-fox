namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     What the first reason of a refusal is about, by the kind of the reason: a number and its limit (the reward to risk and its lowest, the free
///     money and the amount, the sum of the day and its limit, the open trades of a side and their limit) or the holder of the pair.
/// </summary>
public sealed record RefusalDetail(decimal? Actual, decimal? Limit, PairHolder? Holder)
{
    public static RefusalDetail Numbers(decimal actual, decimal limit)
    {
        return new RefusalDetail(actual, limit, null);
    }

    public static RefusalDetail Pair(PairHolder holder)
    {
        return new RefusalDetail(null, null, holder);
    }
}