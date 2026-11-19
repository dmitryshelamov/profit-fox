namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     A covered window of history, [<see cref="From" />, <see cref="To" />) in Unix milliseconds UTC.
/// </summary>
public sealed record PreviewRange(long From, long To);
