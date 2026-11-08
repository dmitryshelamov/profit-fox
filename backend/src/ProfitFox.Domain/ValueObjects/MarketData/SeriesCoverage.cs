namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The border of the coverage of a series of candles: inside <c>[From, Until)</c> storage holds every candle the exchange gave, so a
///     candle missing there was not traded (a night, a weekend, a quiet minute) and is never asked for again.
/// </summary>
public sealed record SeriesCoverage
{
    public DateTimeOffset From { get; }

    public DateTimeOffset Until { get; }

    /// <exception cref="ArgumentException">The border does not follow the start.</exception>
    public SeriesCoverage(DateTimeOffset from, DateTimeOffset until)
    {
        if (until <= from)
        {
            throw new ArgumentException("The border of the coverage must follow its start.", nameof(until));
        }

        From = from;
        Until = until;
    }

    /// <summary>The coverage with <c>[from, to)</c> added when it touches or overlaps this one; a separate range leaves it as it is.</summary>
    public SeriesCoverage Extend(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < From || from > Until)
        {
            return this;
        }

        return new SeriesCoverage(from < From ? from : From, to > Until ? to : Until);
    }

    /// <summary>The parts of the ranges outside the coverage, in the order given.</summary>
    public IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Uncovered(IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> ranges)
    {
        var uncovered = new List<(DateTimeOffset From, DateTimeOffset To)>();

        foreach ((DateTimeOffset from, DateTimeOffset to) in ranges)
        {
            if (from < From)
            {
                uncovered.Add((from, to < From ? to : From));
            }

            if (to > Until)
            {
                uncovered.Add((from > Until ? from : Until, to));
            }
        }

        return uncovered;
    }
}