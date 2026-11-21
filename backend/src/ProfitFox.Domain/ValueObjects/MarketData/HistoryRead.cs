namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     What a read of one instrument gave: the covered parts of the window and the items inside them, in the order the
///     service sends them.
/// </summary>
public sealed record HistoryRead<T>(IReadOnlyList<TimeRange> Coverage, IReadOnlyList<T> Items);
