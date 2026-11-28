namespace ProfitFox.Presentation.Contracts.Trades;

/// <summary>
///     The settings of the risk module (spec trade-risk «Настройки риска», «Сроки и попытки»): the default account and amount,
///     the leverage, the limits — empty ones are not set —, and the three numbers of time and attempts; with the moment of the
///     last save in milliseconds, empty before the first.
/// </summary>
public sealed record TradeSettingsResponse(
    Guid? DefaultAccountId,
    decimal? DefaultAmount,
    int? Leverage,
    decimal? MaxAmount,
    int? MaxOpenTrades,
    decimal? MaxTotalAmount,
    decimal? MaxLossPercent,
    decimal? MaxDayLoss,
    int ExpiryMarginMinutes,
    int EntryWaitSeconds,
    int StopAttempts,
    long? SavedAt);

/// <summary>
///     The settings to save; an empty time or number of attempts is its default.
/// </summary>
public sealed record TradeSettingsRequest(
    Guid? DefaultAccountId,
    decimal? DefaultAmount,
    int? Leverage,
    decimal? MaxAmount,
    int? MaxOpenTrades,
    decimal? MaxTotalAmount,
    decimal? MaxLossPercent,
    decimal? MaxDayLoss,
    int? ExpiryMarginMinutes,
    int? EntryWaitSeconds,
    int? StopAttempts);

/// <summary>
///     The emergency stop: when it went off, the loss and the limit — empty while entries are allowed —, the pairs of the trades
///     in «Ошибка» it does not close, and when the ban was last lifted; moments in milliseconds.
/// </summary>
public sealed record HaltResponse(long? HaltedAt, decimal? Loss, decimal? Limit, IReadOnlyList<string> NotClosed, long? LiftedAt);

/// <summary>
///     An account the trading Worker polls, and since when in milliseconds.
/// </summary>
public sealed record PolledAccountResponse(Guid Id, string Name, long Since);

/// <summary>
///     The state of the risk module (spec trade-risk «Раздел „Риск“», «Риск в „Сделках“»): whether the limits are set and those
///     that are not; the loss of the day — positive for a loss — with the sum of the outcomes and results it comes from, its
///     limit and the start of its count; the trades that hold their pairs and the sum of their amounts with their limits; the
///     emergency stop; whether a trading Worker works and which; the number of accounts, those on the stream and those polled.
/// </summary>
public sealed record RiskStateResponse(
    bool LimitsSet,
    IReadOnlyList<string> Missing,
    decimal DayLoss,
    decimal DaySum,
    decimal? DayLimit,
    long DayFrom,
    int OpenTrades,
    int? MaxOpenTrades,
    decimal OpenAmount,
    decimal? MaxTotalAmount,
    HaltResponse Halt,
    bool TraderAlive,
    string? Trader,
    int Accounts,
    int Streamed,
    IReadOnlyList<PolledAccountResponse> Polled);
