namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     The state of the position of a pair that the node <c>position</c> gives the graph (spec strategy-execution «Узел
///     позиции», «Состояние закрытой позиции»): the quantity in coins, the money put in, the average price, the price and
///     the moment of the last fill that increased the position. A closed position has a quantity and money of zero and
///     no prices.
/// </summary>
public sealed record PositionState(decimal Quantity, decimal Invested, decimal? AveragePrice, decimal? LastEntryPrice, DateTimeOffset? LastEntryTime)
{
    public static readonly PositionState Closed = new PositionState(0m, 0m, AveragePrice: null, LastEntryPrice: null, LastEntryTime: null);

    public bool Open => Quantity > 0;
}
