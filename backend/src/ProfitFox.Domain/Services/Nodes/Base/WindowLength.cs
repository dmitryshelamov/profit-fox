using System.Globalization;

using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.Services.Nodes.Base;

/// <summary>
///     The input <c>length</c> of <c>window</c>, <c>percentile</c>, <c>hold</c>, <c>atr</c>, <c>rsi</c> and <c>zones</c>
///     (spec strategy-execution «Длина окна»; change add-setup-strategies, design.md, decision 3): a whole number from 1 to
///     <see cref="Maximum" />, and for <c>atr</c> from 2 (<see cref="From" />, task 8.2). It is read at every candle; any
///     other value, and a missing one, gives the node no result.
/// </summary>
internal static class WindowLength
{
    public const string Pin = "length";

    public const int Maximum = 1000;

    /// <summary>
    ///     The length a value on the pin gives, or null when it is not a whole number from 1 to <see cref="Maximum" />.
    /// </summary>
    public static int? Of(decimal? value)
    {
        return Of(value, 1);
    }

    /// <summary>
    ///     The length a value on the pin gives, or null when it is not a whole number from <paramref name="minimum" /> to
    ///     <see cref="Maximum" />.
    /// </summary>
    public static int? Of(decimal? value, int minimum)
    {
        return value is { } number && number >= minimum && number <= Maximum && number == decimal.Truncate(number) ? (int)number : null;
    }

    /// <summary>
    ///     Why a value written on an input of the node is wrong, or null when it is right: a written length is checked with
    ///     the graph, a length from a wire only at the candle.
    /// </summary>
    public static string? Check(string input, GraphValue value)
    {
        return Check(input, value, 1);
    }

    /// <summary>
    ///     The check of a written length for a kind whose shortest length is <paramref name="minimum" />, as <c>atr</c>: the
    ///     package does not count an ATR of one candle (change add-setup-strategies, design.md, decision 3).
    /// </summary>
    public static Func<string, GraphValue, string?> From(int minimum)
    {
        return (input, value) => Check(input, value, minimum);
    }

    private static string? Check(string input, GraphValue value, int minimum)
    {
        if (input != Pin || value.Type != PinType.Number || Of(value.Number, minimum) is not null)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"The length is a whole number from {minimum} to {Maximum}, not {value.Number}.");
    }
}
