using MediatR;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     The state of the live data for the section «Идеи» (spec strategy-ideas «Состояние сканера в разделе „Идеи“»).
/// </summary>
public sealed record GetScannerStateQuery : IRequest<ScannerStateInternal>;

/// <summary>
///     Who keeps the live data and whether the lease holds now, how long nobody has kept it, since when ideas count without a
///     break, the warmed pairs of all, the numbers of open and closed ideas, the thresholds of the burst by timeframe, and how
///     many pairs the warm-up failed on with the last reason (task 18.3).
/// </summary>
public sealed record ScannerStateInternal(
    string? Worker,
    bool Live,
    TimeSpan? Idle,
    DateTimeOffset? LiveSince,
    int Warmed,
    int Pairs,
    int Open,
    int Closed,
    IReadOnlyDictionary<string, int> Burst,
    int Failed = 0,
    string? Failure = null);
