namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The settings of the risk module of the trades by ideas (change take-idea-trades, spec trade-risk «Настройки риска»,
///     «Сроки и попытки»): the account and the amount the entry dialog opens with, the leverage, the limits of the amount of
///     a trade, of the number of open trades and of the amount in all of them, the limit of the loss of a trade in percent
///     and that of the loss of a day in USDT; and the three numbers of time and attempts, which have defaults. A limit not
///     set is null: until the six limits of <see cref="Services.Trades.TradeSettingsRules.Missing" /> are set, no trade is
///     entered. <see cref="SavedAt" /> is null until the settings were first saved.
/// </summary>
public sealed record TradeSettings
{
    public static readonly TimeSpan DefaultExpiryMargin = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultEntryWait = TimeSpan.FromSeconds(30);
    public const int DefaultStopAttempts = 3;

    /// <summary>
    ///     The settings of a fresh bench: no limit, the default times and attempts.
    /// </summary>
    public static readonly TradeSettings Empty = new TradeSettings();

    public Guid? DefaultAccountId { get; init; }

    /// <summary>
    ///     The value of the position in USDT the entry dialog opens with.
    /// </summary>
    public decimal? DefaultAmount { get; init; }

    /// <summary>
    ///     A whole number from 1 to 20.
    /// </summary>
    public int? Leverage { get; init; }

    public decimal? MaxAmount { get; init; }

    public int? MaxOpenTrades { get; init; }

    public decimal? MaxTotalAmount { get; init; }

    /// <summary>
    ///     The limit of the loss of a trade, in percent of the price of the click: the protective stop is not farther.
    /// </summary>
    public decimal? MaxLossPercent { get; init; }

    /// <summary>
    ///     The loss of a day in USDT at which the emergency stop closes every trade.
    /// </summary>
    public decimal? MaxDayLoss { get; init; }

    /// <summary>
    ///     How long before the end of an idea a trade is no longer entered: from 1 to 60 minutes.
    /// </summary>
    public TimeSpan ExpiryMargin { get; init; } = DefaultExpiryMargin;

    /// <summary>
    ///     How long Bybit has to take the entry order after the click: from 10 to 120 seconds.
    /// </summary>
    public TimeSpan EntryWait { get; init; } = DefaultEntryWait;

    /// <summary>
    ///     How many refusals in a row to put the stop of a trade back on the exchange end it: from 1 to 10.
    /// </summary>
    public int StopAttempts { get; init; } = DefaultStopAttempts;

    public DateTimeOffset? SavedAt { get; init; }
}
