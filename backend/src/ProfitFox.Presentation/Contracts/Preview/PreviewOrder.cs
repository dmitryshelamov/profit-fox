namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     An entry order of the strategy (spec strategy-preview «Исход заявки»): its moment in Unix milliseconds UTC, its side
///     and its outcome — <c>opened</c> with its trade, <c>skipped</c> while the position was open, <c>excluded</c> with the
///     reason (<c>plan</c>, <c>coverage</c>, <c>no-trade</c>, <c>funding</c>, <c>period</c>) or <c>not-computed</c>.
/// </summary>
public sealed record PreviewOrder(long Moment, string Side, string Outcome, string? Reason, PreviewTrade? Trade);
