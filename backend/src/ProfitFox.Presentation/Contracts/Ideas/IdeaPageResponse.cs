namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A page of ideas (change add-ideas-and-market, design.md, decision 10): its rows, the number of all, the page and its
///     size, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaPageResponse(IReadOnlyList<IdeaResponse> Items, int Total, int Page, int PageSize, bool FilterGone);

/// <summary>
///     A row of the summary of the journal: a strategy with its version and a timeframe, the number of closed ideas, the share
///     of those in the plus and the mean outcome as fractions, and the sum in USDT.
/// </summary>
public sealed record IdeaSummaryResponse(Guid StrategyId, string Strategy, int Version, string Timeframe, int Ideas, double Wins, double Mean, double Sum);

public sealed record IdeaSummaryPageResponse(IReadOnlyList<IdeaSummaryResponse> Items, int Total, int Page, int PageSize, bool FilterGone);

/// <summary>
///     The state of the live data (spec strategy-ideas «Состояние сканера в разделе „Идеи“»): the Worker that keeps it and
///     whether its lease holds now, the seconds nobody has kept it — null when nobody ever did —, since when ideas count
///     without a break, the warmed pairs of all, the numbers of open and closed ideas, the thresholds of the burst, and how
///     many pairs the warm-up failed on with the last reason — null while none failed (task 18.3).
/// </summary>
public sealed record ScannerStateResponse(
    string? Worker,
    bool Live,
    double? IdleSeconds,
    long? LiveSince,
    int Warmed,
    int Pairs,
    int Open,
    int Closed,
    IReadOnlyDictionary<string, int> Burst,
    int Failed,
    string? Failure);
