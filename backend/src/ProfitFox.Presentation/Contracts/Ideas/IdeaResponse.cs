namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     An idea (change add-ideas-and-market, spec strategy-ideas «Идея»): its pair, strategy with version, timeframe, side
///     (<c>long</c>, <c>short</c>), labels, moment and end of time in milliseconds, size, the entry it shows — the close of
///     its candle until the entry —, the stop and the target as fractions and in prices, the result now as a fraction, its
///     state (<c>open</c>, <c>closed</c>, <c>excluded</c>), the exit with its reason (<c>stop</c>, <c>target</c>,
///     <c>time</c>), the outcome as a fraction and in USDT, why an excluded idea is, and its score by the weights now (change
///     add-idea-score; 0 outside the lists), a number with at most two digits after the point (change decimal-label-weights),
///     and its trade — the latest not cancelled, by its id and state — or none (change take-idea-trades, design.md, decision 13).
/// </summary>
public sealed record IdeaResponse(
    Guid Id,
    string Symbol,
    Guid StrategyId,
    string Strategy,
    int Version,
    string Timeframe,
    string Side,
    IReadOnlyList<string> Labels,
    long Moment,
    long Until,
    double Size,
    double Entry,
    long? EntryTime,
    double? StopFraction,
    double? TargetFraction,
    double? Stop,
    double? Target,
    double? Now,
    string State,
    long? ExitTime,
    string? ExitReason,
    bool Ambiguous,
    double? Result,
    double? ResultUsdt,
    string? Excluded,
    decimal Score,
    IdeaTradeResponse? Trade = null);

/// <summary>
///     The trade of an idea: its id and state (<c>pending</c>, <c>open</c>, <c>closing</c>, <c>closed</c>, <c>error</c>).
/// </summary>
public sealed record IdeaTradeResponse(Guid Id, string State);
