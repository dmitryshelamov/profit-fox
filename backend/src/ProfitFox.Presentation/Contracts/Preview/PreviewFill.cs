namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     A fill of a cycle: its moment in Unix milliseconds UTC, what it did — <c>entry</c>, <c>increase</c>, <c>decrease</c>
///     or <c>exit</c> — its side, <c>buy</c> or <c>sell</c>, its price with the slippage, the quantity in coins, the amount
///     in USDT and the node of its order (none for the exit of a trade of a plan by its time, stop or target).
/// </summary>
public sealed record PreviewFill(long Moment, string Action, string Side, double Price, double Quantity, double Amount, string? Node);
