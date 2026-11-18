namespace ProfitFox.Engine.Simulation.Plans;

/// <summary>
///     The outcome of one trade of a plan, as fractions of the trade amount; every value but the status and the entry moment is null when
///     the trade is excluded.
/// </summary>
/// <param name="EntryMoment">The moment of the entry: the close of the candle of the event.</param>
/// <param name="Status">Whether the trade has an outcome.</param>
/// <param name="EntryTime">When the entry filled.</param>
/// <param name="EntryPrice">The price of the entry after slippage.</param>
/// <param name="ExitTime">When the exit filled.</param>
/// <param name="ExitPrice">The price of the exit after slippage.</param>
/// <param name="Gross">The result at the opens of the entry and exit minutes, before costs.</param>
/// <param name="Slippage">What the slippage of both fills took.</param>
/// <param name="Fees">The taker fees of both fills.</param>
/// <param name="Funding">The funding received; below zero when paid.</param>
/// <param name="Net">The result after slippage, fees and funding.</param>
public sealed record TradeOutcome(
    DateTimeOffset EntryMoment,
    TradeStatus Status,
    DateTimeOffset? EntryTime = null,
    decimal? EntryPrice = null,
    DateTimeOffset? ExitTime = null,
    decimal? ExitPrice = null,
    decimal? Gross = null,
    decimal? Slippage = null,
    decimal? Fees = null,
    decimal? Funding = null,
    decimal? Net = null);