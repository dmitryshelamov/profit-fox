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

/// <summary>
///     A row of the outcomes by score (change add-idea-score, spec idea-score «Итоги по баллу»): the score, the number of
///     closed ideas, the share of those in the plus and the mean outcome after costs as fractions, and the sum in USDT.
/// </summary>
public sealed record IdeaScoreResponse(int Score, int Ideas, double Wins, double Mean, double Sum);

/// <summary>
///     The outcomes by score, from the highest score, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaScoresResponse(IReadOnlyList<IdeaScoreResponse> Rows, bool FilterGone);

public sealed record IdeaSummaryPageResponse(IReadOnlyList<IdeaSummaryResponse> Items, int Total, int Page, int PageSize, bool FilterGone);

/// <summary>
///     The state of the live data (spec strategy-ideas «Состояние сканера в разделе „Идеи“»): the Worker that keeps it and
///     whether its lease holds now, the seconds nobody has kept it — null when nobody ever did —, since when ideas count
///     without a break, the warmed pairs of all, the numbers of open and closed ideas, the thresholds of the burst, and how
///     many pairs the warm-up failed on with the last reason — null while none failed (task 18.3) —, and the seconds the
///     warm-up has still to go at its speed so far — null when it is not known or over.
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
    string? Failure,
    double? WarmupLeftSeconds);

/// <summary>
///     How many ideas a strategy has among those counted (change add-setup-strategies, spec strategy-ideas «Число идей по
///     стратегиям»).
/// </summary>
public sealed record IdeaStrategyCountResponse(string Strategy, int Ideas);

/// <summary>
///     The number of all the ideas counted, a row for every strategy of a category of ideas and for every strategy of the
///     ideas counted, by name, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaStrategyCountsResponse(int Total, IReadOnlyList<IdeaStrategyCountResponse> Strategies, bool FilterGone);
