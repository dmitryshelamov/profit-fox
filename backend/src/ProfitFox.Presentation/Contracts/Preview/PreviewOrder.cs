namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     An order of the strategy (spec strategy-preview «Исход заявки»): its moment in Unix milliseconds UTC, its node, its
///     side — <c>long</c> or <c>short</c> for an order of <c>enter</c>, <c>buy</c> or <c>sell</c> for a fill of the other
///     nodes — and its outcome: <c>filled</c> with its price, amount in USDT and the index of its cycle, <c>skipped</c>
///     while the position was open, <c>excluded</c> with the reason (<c>plan</c>, <c>coverage</c>, <c>no-trade</c>,
///     <c>funding</c>, <c>period</c>) or <c>not-computed</c>.
/// </summary>
public sealed record PreviewOrder(long Moment, string? Node, string Side, string Outcome, string? Reason, double? Price, double? Amount, int? Cycle);
