namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     Who keeps the live data of the bench (change add-ideas-and-market, spec strategy-ideas «Состояние сканера в разделе
///     „Идеи“»): the Worker and until when its lease holds, since when ideas are counted without a break — null while it
///     catches up —, how many pairs are warmed up and how many there are.
/// </summary>
public sealed record ScannerState(string Worker, DateTimeOffset Until, DateTimeOffset? LiveSince, int Warmed, int Pairs);
