namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     A cycle of the position (spec strategy-preview «Циклы в ответе предпросмотра»): its side, <c>long</c> or
///     <c>short</c>, its fills, the number of entries, the largest money put in, how it closed — <c>stop</c>,
///     <c>target</c>, <c>time</c>, <c>limit</c>, <c>market</c>, <c>close</c>, or none while it is open — whether the order of
///     fills in a minute was ambiguous, and the result after costs as a fraction of the largest money put in and in USDT. An
///     open cycle has the estimate at the last price instead of the result; a cycle left out on a hole in the history has
///     <see cref="Exclusion" /> <c>coverage</c> and no result.
/// </summary>
public sealed record PreviewCycle(
    string Side,
    IReadOnlyList<PreviewFill> Fills,
    int Entries,
    double Invested,
    string? ExitReason,
    bool Ambiguous,
    double? Net,
    double? NetAmount,
    bool Open,
    string? Exclusion);
