namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     An order a node holds and that has not filled yet: its node, side, limit price (none for a market order), size and
///     whether the size is in coins, and the moment it was placed. A limit order is the same object for as long as its
///     price and size stay (spec strategy-execution «Лимитная заявка»).
/// </summary>
public sealed class StandingOrder
{
    public required string Node { get; init; }

    public required FillSide Side { get; init; }

    public double? Price { get; init; }

    public required double Size { get; init; }

    public bool SizeInQuantity { get; init; }

    public required DateTimeOffset Placed { get; init; }
}
