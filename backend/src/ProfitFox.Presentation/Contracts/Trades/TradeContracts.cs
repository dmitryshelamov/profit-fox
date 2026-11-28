namespace ProfitFox.Presentation.Contracts.Trades;

/// <summary>
///     The entry asked for (change take-idea-trades, design.md, decision 13): the idea, and the account and the amount in USDT —
///     empty ones are taken from the settings of the risk module.
/// </summary>
public sealed record EntryRequest(Guid IdeaId, Guid? AccountId, decimal? Amount);

/// <summary>
///     A check before the entry: its code, whether it passed, and what the dialog shows.
/// </summary>
public sealed record EntryCheckResponse(string Code, bool Passed, string Text);

/// <summary>
///     The count of an entry (spec idea-trades «Расчёт перед входом»): the side (<c>long</c>, <c>short</c>), the price now,
///     the amount, the leverage, the quantity, the value of the position and its margin; the entry, stop and target of the idea;
///     the stop of the trade, where it comes from (<c>idea</c>, <c>limit</c>) and the price at the limit, the target, their
///     distances from the price now as signed fractions; the loss at the stop and the profit at the target with fees; the
///     estimated liquidation, the free USDT needed, the end in milliseconds; the account and its free USDT; every check and
///     whether all passed.
/// </summary>
public sealed record EntryPreviewResponse(
    string Side,
    decimal Price,
    decimal Amount,
    int Leverage,
    decimal Quantity,
    decimal Value,
    decimal Margin,
    decimal IdeaEntry,
    decimal? IdeaStop,
    decimal? IdeaTarget,
    decimal? Stop,
    string? StopSource,
    decimal? LimitStop,
    decimal? StopDistance,
    decimal? Target,
    decimal? TargetDistance,
    decimal? LossAtStop,
    decimal? ProfitAtTarget,
    decimal Liquidation,
    decimal Needed,
    long Until,
    Guid? AccountId,
    string? AccountName,
    decimal? Free,
    IReadOnlyList<EntryCheckResponse> Checks,
    bool Passed);

/// <summary>
///     The entry was refused: the checks that did not pass.
/// </summary>
public sealed record EntryRefusedResponse(IReadOnlyList<EntryCheckResponse> Checks);

/// <summary>
///     A trade by an idea (spec idea-trades «Раздел „Сделки“»): its idea as it was at the click, the account by id — empty once
///     removed — and name, the amount and the leverage, the price of the click, the quantity of the entry and what is held,
///     the stop with its source, the target; the state (<c>pending</c>, <c>open</c>, <c>closing</c>, <c>closed</c>,
///     <c>cancelled</c>, <c>error</c>) with its reason and the reason of the close (<c>target</c>, <c>stop</c>, <c>time</c>,
///     <c>manual</c>, <c>halt</c>, <c>no-stop</c>, <c>liquidation</c>, <c>adl</c>, <c>exchange</c>); the mean prices of the entry
///     and the exit, the fees, the funding, the outcome in USDT and as a fraction of the amount, the result now and when it was
///     counted; the moments of the click, the entry and the close; and the outcome of its idea as a fraction. Moments are in
///     milliseconds.
/// </summary>
public sealed record TradeResponse(
    Guid Id,
    Guid IdeaId,
    string Symbol,
    string Side,
    string Strategy,
    int Version,
    string Timeframe,
    long IdeaMoment,
    long Until,
    decimal IdeaEntry,
    Guid? AccountId,
    string AccountName,
    decimal Amount,
    int Leverage,
    decimal ClickPrice,
    decimal Quantity,
    decimal Held,
    decimal Stop,
    string StopSource,
    decimal? Target,
    string State,
    string? Reason,
    string? CloseReason,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal Fees,
    decimal Funding,
    decimal? Result,
    decimal? ResultShare,
    decimal? Unrealised,
    long? MarkedAt,
    long CreatedAt,
    long? OpenedAt,
    long? ClosedAt,
    double? IdeaResult);

/// <summary>
///     A page of trades, the number of all of its tab, the page and its size, and the numbers of the open and of the closed.
/// </summary>
public sealed record TradePageResponse(IReadOnlyList<TradeResponse> Items, int Total, int Page, int PageSize, int Open, int Closed);
