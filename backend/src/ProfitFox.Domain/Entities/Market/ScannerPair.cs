using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.Market;

/// <summary>
///     An instrument the scanner keeps, and how much of its data storage holds: every minute candle before
///     <see cref="CoveredUntil" /> is known, a missing one is a minute without trades; the same for the points of open interest and
///     the funding rates. <see cref="HistoryFrom" /> is set once the history of the pair is loaded whole. A pair that is not
///     <see cref="Listed" /> is out of trading: it stays with its data but is not kept. <see cref="ListedAt" /> is the launch the
///     exchange tells (linear only): the history does not go before it. <see cref="TickSize" /> is the step of its price as the list of the
///     exchange last told it; the ideas of a pair wait for it. <see cref="TradFi" /> tells a non-crypto pair (a stock, an ETF, a commodity
///     or forex) as the list of the exchange last told it; a pair the list has not told about yet is crypto.
///     <see cref="QuantityStep" />, <see cref="MinQuantity" /> and <see cref="MinAmount" /> are the step and the minimums of its orders, which the paper
///     trades of the autos are sized by.
/// </summary>
public sealed record ScannerPair(
    Instrument Instrument,
    bool Listed,
    DateTimeOffset? HistoryFrom = null,
    DateTimeOffset? CoveredUntil = null,
    DateTimeOffset? OpenInterestCoveredUntil = null,
    DateTimeOffset? FundingCoveredUntil = null,
    DateTimeOffset? ListedAt = null,
    decimal? TickSize = null,
    bool TradFi = false,
    decimal? QuantityStep = null,
    decimal? MinQuantity = null,
    decimal? MinAmount = null)
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    /// <summary>A closed minute right at the bound moves it by the minute; one after a gap leaves it, the gap is loaded later.</summary>
    public ScannerPair WithClosedMinute(DateTimeOffset openTime)
    {
        return openTime == CoveredUntil ? this with { CoveredUntil = openTime + Minute } : this;
    }

    /// <summary>
    ///     A load of every minute in <c>[from, to)</c> that starts inside the coverage, or the first one of the pair, moves the bound to its
    ///     end; the minutes it did not bring had no trades.
    /// </summary>
    public ScannerPair WithLoaded(DateTimeOffset from, DateTimeOffset to)
    {
        return CoveredUntil is null || (from <= CoveredUntil && to > CoveredUntil) ? this with { CoveredUntil = to } : this;
    }

    /// <summary>Like <see cref="WithLoaded" /> for the points of open interest, whose bound is the next 5 minute step not known.</summary>
    public ScannerPair WithOpenInterestLoaded(DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset next = to.AddTicks((Step.Ticks - (to.UtcTicks % Step.Ticks)) % Step.Ticks);

        return OpenInterestCoveredUntil is null || (from <= OpenInterestCoveredUntil && next > OpenInterestCoveredUntil)
            ? this with { OpenInterestCoveredUntil = next }
            : this;
    }

    /// <summary>The point of the step right at the bound, as the ticker gives it, moves the bound by a step.</summary>
    public ScannerPair WithOpenInterestPoint(DateTimeOffset time)
    {
        return time == OpenInterestCoveredUntil ? this with { OpenInterestCoveredUntil = time + Step } : this;
    }

    /// <summary>Whether storage misses some minute before <paramref name="end" />, the end of the last closed minute.</summary>
    public bool IsBehind(DateTimeOffset end)
    {
        return !(CoveredUntil >= end);
    }
}