using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Settings of a node that change its pins or behavior; each kind reads only its own.</summary>
/// <param name="ValueType">Type of the values a compare node takes; numbers when not set.</param>
/// <param name="Operation"></param>
/// <param name="Side"></param>
/// <param name="SizeMode">Whether the size of an order node is a quantity of the base asset or an amount of the quote asset.</param>
public sealed record NodeSettings(PinType? ValueType = null, CompareOperation? Operation = null, OrderSide? Side = null, OrderSizeMode? SizeMode = null);