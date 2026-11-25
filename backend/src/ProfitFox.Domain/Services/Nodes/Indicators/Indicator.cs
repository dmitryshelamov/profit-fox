namespace ProfitFox.Domain.Services.Nodes.Indicators;

/// <summary>
///     What <c>atr</c> and <c>rsi</c> share (change add-setup-strategies, design.md, decision 1): the package lists keep
///     only the last results — their state of the smoothing is apart from the results, and a list pruned to
///     <see cref="KeptResults" /> gives the same numbers as the batch <c>ToAtr</c> and <c>ToRsi</c> (task 2.6) — and the
///     result in a decimal of at most 15 significant digits, as the explicit conversion of a double gives it.
/// </summary>
internal static class Indicator
{
    /// <summary>
    ///     How many results a list keeps; the package keeps 100 000 unless told otherwise.
    /// </summary>
    public const int KeptResults = 2;

    /// <summary>
    ///     The candles kept to start a list again when the length changes within a history.
    /// </summary>
    public const int KeptCandles = 1000;

    /// <summary>
    ///     The value of the package as a decimal, or null when it has none or it is not a finite number a decimal holds.
    /// </summary>
    public static decimal? ToDecimal(double? value)
    {
        if (value is not { } number || !double.IsFinite(number))
        {
            return null;
        }

        try
        {
            return (decimal)number;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
