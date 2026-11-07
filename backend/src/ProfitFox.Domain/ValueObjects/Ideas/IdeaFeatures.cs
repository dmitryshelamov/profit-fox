namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The features of a signal known on the minute it fired (decision 1 of add-idea-features): a strong move of the window of its setup and a
///     high volume of its candle; their four combinations are the levels the cases of a bucket split into.
/// </summary>
public readonly record struct IdeaFeatures(bool StrongMove, bool HighVolume)
{
    /// <summary>A move in ATR of a strong move, from which it counts.</summary>
    private const decimal StrongMoveFrom = 4m;

    /// <summary>A volume in medians of a high volume, from which it counts.</summary>
    private const decimal HighVolumeFrom = 4m;

    public static IdeaFeatures Of(decimal move, decimal volumeRatio)
    {
        return new IdeaFeatures(move >= StrongMoveFrom, volumeRatio >= HighVolumeFrom);
    }
}