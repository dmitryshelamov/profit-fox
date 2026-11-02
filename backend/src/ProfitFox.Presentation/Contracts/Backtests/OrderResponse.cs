namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>An order of a position with its fills.</summary>
/// <param name="Id"></param>
/// <param name="Node">The path of the node that placed it; <c>null</c> when the closing position placed it.</param>
/// <param name="Side"></param>
/// <param name="Type"></param>
/// <param name="Price">The limit price; <c>null</c> for a market order.</param>
/// <param name="Quantity"></param>
/// <param name="Amount">The amount of a market order sized by amount.</param>
/// <param name="Status"><c>filled</c> or <c>active</c> at the end of the period.</param>
/// <param name="Fills"></param>
public sealed record OrderResponse(
    string Id,
    string? Node,
    string Side,
    string Type,
    string? Price,
    string? Quantity,
    string? Amount,
    string Status,
    IReadOnlyList<FillResponse> Fills);