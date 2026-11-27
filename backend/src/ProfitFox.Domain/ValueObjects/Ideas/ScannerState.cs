namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     Who keeps the live data of the bench (change add-ideas-and-market, spec strategy-ideas «Состояние сканера в разделе
///     „Идеи“»): the Worker and until when its lease holds, since when ideas are counted without a break — null while it
///     catches up —, how many pairs are warmed up and how many there are, and how many pairs the warm-up failed on with the
///     last reason (task 18.3), and how long the warm-up has still to go — null when it is not known or over.
/// </summary>
public sealed record ScannerState(
    string Worker,
    DateTimeOffset Until,
    DateTimeOffset? LiveSince,
    int Warmed,
    int Pairs,
    int Failed = 0,
    string? Failure = null,
    TimeSpan? WarmupLeft = null);
