using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.Strategies;

/// <summary>Codes of the strategy enums in the API and in stored definitions: <c>market-order</c>, <c>number</c>, <c>long</c>, <c>buy</c>, <c>ge</c> and so on.</summary>
public static class StrategyCodes
{
    private static readonly Dictionary<Type, Dictionary<string, Enum>> ValuesByCode = new Dictionary<Type, Dictionary<string, Enum>>
    {
        [typeof(SimpleNodeKind)] = Map(
            ("parameters", SimpleNodeKind.Parameters),
            ("inputs", SimpleNodeKind.Inputs),
            ("outputs", SimpleNodeKind.Outputs),
            ("candle", SimpleNodeKind.Candle),
            ("position", SimpleNodeKind.Position),
            ("ema", SimpleNodeKind.Ema),
            ("cross", SimpleNodeKind.Cross),
            ("add", SimpleNodeKind.Add),
            ("subtract", SimpleNodeKind.Subtract),
            ("multiply", SimpleNodeKind.Multiply),
            ("divide", SimpleNodeKind.Divide),
            ("compare", SimpleNodeKind.Compare),
            ("and", SimpleNodeKind.And),
            ("or", SimpleNodeKind.Or),
            ("not", SimpleNodeKind.Not),
            ("market-order", SimpleNodeKind.MarketOrder),
            ("close-position", SimpleNodeKind.ClosePosition),
            ("hold-limit", SimpleNodeKind.HoldLimit)),
        [typeof(PinType)] = Map(("number", PinType.Number), ("boolean", PinType.Boolean), ("duration", PinType.Duration)),
        [typeof(StrategyDirection)] = Map(("long", StrategyDirection.Long), ("short", StrategyDirection.Short)),
        [typeof(OrderSide)] = Map(("buy", OrderSide.Buy), ("sell", OrderSide.Sell)),
        [typeof(OrderSizeMode)] = Map(("quantity", OrderSizeMode.Quantity), ("amount", OrderSizeMode.Amount)),
        [typeof(CompareOperation)] = Map(
            ("gt", CompareOperation.Greater),
            ("ge", CompareOperation.GreaterOrEqual),
            ("lt", CompareOperation.Less),
            ("le", CompareOperation.LessOrEqual)),
        [typeof(BuiltInNode)] = Map(("ema-cross", BuiltInNode.EmaCross), ("ladder", BuiltInNode.Ladder), ("dca", BuiltInNode.Dca)),
        [typeof(UsageKind)] = Map(("strategy", UsageKind.Strategy), ("node", UsageKind.Composite)),
        [typeof(OrderType)] = Map(("market", OrderType.Market), ("limit", OrderType.Limit))
    };

    private static readonly Dictionary<Enum, string> CodesByValue = ValuesByCode.Values.SelectMany(map => map).ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParse(Type type, string code, out Enum? value)
    {
        return ValuesByCode[type].TryGetValue(code, out value);
    }

    public static bool TryParse<T>(string code, out T value)
        where T : struct, Enum
    {
        bool parsed = TryParse(typeof(T), code, out Enum? found);
        value = parsed ? (T)found! : default;

        return parsed;
    }

    public static string ToCode(Enum value)
    {
        return CodesByValue[value];
    }

    private static Dictionary<string, Enum> Map(params (string Code, Enum Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Code, pair => pair.Value);
    }
}