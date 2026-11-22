using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     An order of a strategy with orders and what became of it (spec strategy-preview «Исход заявки»): every order of a
///     node <c>enter</c> and every fill of the orders of the other nodes, with its moment, node and side; a fill has its
///     price, its amount in USDT and the index of its cycle. An order of another node taken off or replaced without a fill
///     is not here.
/// </summary>
public sealed record PositionOrder(
    DateTimeOffset Moment,
    string Node,
    FillSide Side,
    OrderResult Result,
    OrderExclusion? Exclusion,
    double? Price,
    double? Amount,
    int? Cycle);
