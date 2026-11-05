using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>Templates that buy at the market on a signal of the indicators of the candles and leave on the opposite signal or on a stop.</summary>
internal static class IndicatorTemplates
{
    public static StrategyDefinition EmaCrossover()
    {
        GraphSketch sketch = Skeleton()
            .Composite("cross", BuiltInNode.EmaCross)
            .Wire("candle.close", "cross.source")
            .Wire("params.fast", "cross.fast")
            .Wire("params.slow", "cross.slow");

        return Long(
            [TemplateParts.Period("fast", 12m), TemplateParts.Period("slow", 26m), TemplateParts.Number("amount", 100m), TemplateParts.Share("stopLoss", 0.05m)],
            LeaveOnAny(Stop(Enter(sketch, "cross.up"), "position.lastEntryPrice"), "cross.down", "stopHit.result"));
    }

    public static StrategyDefinition SmaCrossover()
    {
        GraphSketch sketch = Skeleton()
            .Node("fastSma", SimpleNodeKind.Sma)
            .Node("slowSma", SimpleNodeKind.Sma)
            .Node("cross", SimpleNodeKind.Cross)
            .Wire("candle.close", "fastSma.source")
            .Wire("params.fast", "fastSma.period")
            .Wire("candle.close", "slowSma.source")
            .Wire("params.slow", "slowSma.period")
            .Wire("fastSma.value", "cross.a")
            .Wire("slowSma.value", "cross.b");

        return Long(
            [TemplateParts.Period("fast", 20m), TemplateParts.Period("slow", 50m), TemplateParts.Number("amount", 100m)],
            LeaveOnAny(Enter(sketch, "cross.up"), "cross.down"));
    }

    public static StrategyDefinition MacdTrend()
    {
        GraphSketch sketch = Skeleton()
            .Composite("macd", BuiltInNode.Macd)
            .Node("cross", SimpleNodeKind.Cross)
            .Node("trend", SimpleNodeKind.Ema)
            .Node("above", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.Greater))
            .Node("buySignal", SimpleNodeKind.And)
            .Wire("candle.close", "macd.source")
            .Wire("params.fast", "macd.fast")
            .Wire("params.slow", "macd.slow")
            .Wire("params.signal", "macd.signal")
            .Wire("macd.macd", "cross.a")
            .Wire("macd.signal", "cross.b")
            .Wire("candle.close", "trend.source")
            .Wire("params.trendPeriod", "trend.period")
            .Wire("candle.close", "above.a")
            .Wire("trend.value", "above.b")
            .Wire("cross.up", "buySignal.a")
            .Wire("above.result", "buySignal.b");

        return Long(
            [
                TemplateParts.Period("fast", 12m),
                TemplateParts.Period("slow", 26m),
                TemplateParts.Period("signal", 9m),
                TemplateParts.Period("trendPeriod", 200m),
                TemplateParts.Number("amount", 100m),
                TemplateParts.Share("stopLoss", 0.05m)
            ],
            LeaveOnAny(Stop(Enter(sketch, "buySignal.result"), "position.lastEntryPrice"), "cross.down", "stopHit.result"));
    }

    /// <summary>Buys when the RSI rises over the entry level, leaves when it reaches the exit level.</summary>
    public static StrategyDefinition RsiReversion()
    {
        GraphSketch sketch = Skeleton()
            .Node("rsi", SimpleNodeKind.Rsi)
            .Node("previousRsi", SimpleNodeKind.Previous)
            .Node("wasLow", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
            .Node("isAbove", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.Greater))
            .Node("crossUp", SimpleNodeKind.And)
            .Node("high", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Wire("candle.close", "rsi.source")
            .Wire("params.rsiPeriod", "rsi.period")
            .Wire("rsi.value", "previousRsi.value")
            .Wire("previousRsi.value", "wasLow.a")
            .Wire("params.rsiEntry", "wasLow.b")
            .Wire("rsi.value", "isAbove.a")
            .Wire("params.rsiEntry", "isAbove.b")
            .Wire("wasLow.result", "crossUp.a")
            .Wire("isAbove.result", "crossUp.b")
            .Wire("rsi.value", "high.a")
            .Wire("params.rsiExit", "high.b");

        return Long(
            [
                TemplateParts.Period("rsiPeriod", 14m),
                TemplateParts.Number("rsiEntry", 30m, 0m, 100m),
                TemplateParts.Number("rsiExit", 70m, 0m, 100m),
                TemplateParts.Number("amount", 100m),
                TemplateParts.Share("stopLoss", 0.05m)
            ],
            LeaveOnAny(Stop(Enter(sketch, "crossUp.result"), "position.lastEntryPrice"), "high.result", "stopHit.result"));
    }

    /// <summary>Buys when the close falls under the lower band, leaves when it is back at the middle band.</summary>
    public static StrategyDefinition BollingerReversion()
    {
        GraphSketch sketch = Skeleton()
            .Composite("bands", BuiltInNode.Bollinger)
            .Node("below", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.Less))
            .Node("back", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Wire("candle.close", "bands.source")
            .Wire("params.period", "bands.period")
            .Wire("params.width", "bands.width")
            .Wire("candle.close", "below.a")
            .Wire("bands.lower", "below.b")
            .Wire("candle.close", "back.a")
            .Wire("bands.middle", "back.b");

        return Long(
            [TemplateParts.Period("period", 20m), TemplateParts.Number("width", 2m), TemplateParts.Number("amount", 100m), TemplateParts.Share("stopLoss", 0.05m)],
            LeaveOnAny(Stop(Enter(sketch, "below.result"), "position.lastEntryPrice"), "back.result", "stopHit.result"));
    }

    /// <summary>Buys a fall of the close under the open on a volume over the average of the bars before; leaves on the take profit or after <c>maxHold</c> bars.</summary>
    public static StrategyDefinition CapitulationBuyback()
    {
        GraphSketch sketch = Skeleton()
            .Node("dropFactor", SimpleNodeKind.Subtract, null, TemplateParts.Constant("a", 1m))
            .Node("dropLevel", SimpleNodeKind.Multiply)
            .Node("isDrop", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
            .Node("setup", SimpleNodeKind.And)
            .Wire("params.drop", "dropFactor.b")
            .Wire("candle.open", "dropLevel.a")
            .Wire("dropFactor.result", "dropLevel.b")
            .Wire("candle.close", "isDrop.a")
            .Wire("dropLevel.result", "isDrop.b")
            .Wire("isDrop.result", "setup.a")
            .Wire("volumeOk.result", "setup.b");

        return Long(
            [
                TemplateParts.Number("amount", 100m),
                TemplateParts.Share("drop", 0.06m),
                TemplateParts.Number("volumeMultiple", 2m),
                TemplateParts.Period("volumePeriod", 50m),
                TemplateParts.Number("takeProfit", 0.1m),
                TemplateParts.Period("maxHold", 18m)
            ],
            LeaveOnAny(TakeProfit(MaxHold(Enter(Volume(sketch), "setup.result"))), "hitTarget.result", "timeUp.result"));
    }

    /// <summary>Buys a rise of the close over the open on a volume over the average of the bars before; leaves on the target, the stop or after <c>maxHold</c> bars.</summary>
    public static StrategyDefinition VolumeImpulse()
    {
        GraphSketch sketch = Skeleton()
            .Node("riseFactor", SimpleNodeKind.Add, null, TemplateParts.Constant("a", 1m))
            .Node("riseLevel", SimpleNodeKind.Multiply)
            .Node("isImpulse", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Node("setup", SimpleNodeKind.And)
            .Wire("params.impulse", "riseFactor.b")
            .Wire("candle.open", "riseLevel.a")
            .Wire("riseFactor.result", "riseLevel.b")
            .Wire("candle.close", "isImpulse.a")
            .Wire("riseLevel.result", "isImpulse.b")
            .Wire("isImpulse.result", "setup.a")
            .Wire("volumeOk.result", "setup.b");

        return Long(
            [
                TemplateParts.Number("amount", 100m),
                TemplateParts.Share("impulse", 0.05m),
                TemplateParts.Number("volumeMultiple", 2m),
                TemplateParts.Period("volumePeriod", 50m),
                TemplateParts.Number("takeProfit", 0.1m),
                TemplateParts.Share("stopLoss", 0.05m),
                TemplateParts.Period("maxHold", 12m)
            ],
            LeaveOnAny(Stop(TakeProfit(MaxHold(Enter(Volume(sketch), "setup.result"))), "position.lastEntryPrice"), "hitTarget.result", "stopHit.result", "timeUp.result"));
    }

    private static StrategyDefinition Long(IReadOnlyList<StrategyParameter> parameters, GraphSketch sketch)
    {
        return new StrategyDefinition(StrategyDirection.Long, parameters, sketch.Build());
    }

    /// <summary>The parameters node, the candle, the position and «no position».</summary>
    private static GraphSketch Skeleton()
    {
        return new GraphSketch()
            .Node("params", SimpleNodeKind.Parameters)
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Wire("position.hasPosition", "flat.value");
    }

    /// <summary>Buys <c>amount</c> at the market on <paramref name="signal" /> while there is no position.</summary>
    private static GraphSketch Enter(GraphSketch sketch, string signal)
    {
        return sketch
            .Node("entry", SimpleNodeKind.And)
            .Node("enter", SimpleNodeKind.MarketOrder, TemplateParts.Buy)
            .Wire(signal, "entry.a")
            .Wire("flat.result", "entry.b")
            .Wire("entry.result", "enter.when")
            .Wire("params.amount", "enter.size");
    }

    /// <summary>The close at or below <c>stopLoss</c> under <paramref name="basis" />.</summary>
    private static GraphSketch Stop(GraphSketch sketch, string basis)
    {
        return sketch
            .Node("stopFactor", SimpleNodeKind.Subtract, null, TemplateParts.Constant("a", 1m))
            .Node("stopPrice", SimpleNodeKind.Multiply)
            .Node("stopHit", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.LessOrEqual))
            .Wire("params.stopLoss", "stopFactor.b")
            .Wire(basis, "stopPrice.a")
            .Wire("stopFactor.result", "stopPrice.b")
            .Wire("candle.close", "stopHit.a")
            .Wire("stopPrice.result", "stopHit.b");
    }

    /// <summary>The volume of the bar at or over <c>volumeMultiple</c> times the average volume of the <c>volumePeriod</c> bars before it.</summary>
    private static GraphSketch Volume(GraphSketch sketch)
    {
        return sketch
            .Node("volumeAverage", SimpleNodeKind.Sma)
            .Node("previousAverage", SimpleNodeKind.Previous)
            .Node("volumeLevel", SimpleNodeKind.Multiply)
            .Node("volumeOk", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Wire("candle.volume", "volumeAverage.source")
            .Wire("params.volumePeriod", "volumeAverage.period")
            .Wire("volumeAverage.value", "previousAverage.value")
            .Wire("previousAverage.value", "volumeLevel.a")
            .Wire("params.volumeMultiple", "volumeLevel.b")
            .Wire("candle.volume", "volumeOk.a")
            .Wire("volumeLevel.result", "volumeOk.b");
    }

    /// <summary>The close at or over <c>takeProfit</c> above the last entry.</summary>
    private static GraphSketch TakeProfit(GraphSketch sketch)
    {
        return sketch
            .Node("gainFactor", SimpleNodeKind.Add, null, TemplateParts.Constant("a", 1m))
            .Node("targetPrice", SimpleNodeKind.Multiply)
            .Node("hitTarget", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Wire("params.takeProfit", "gainFactor.b")
            .Wire("position.lastEntryPrice", "targetPrice.a")
            .Wire("gainFactor.result", "targetPrice.b")
            .Wire("candle.close", "hitTarget.a")
            .Wire("targetPrice.result", "hitTarget.b");
    }

    /// <summary><c>maxHold</c> bars closed since the last entry.</summary>
    private static GraphSketch MaxHold(GraphSketch sketch)
    {
        return sketch
            .Node("timeUp", SimpleNodeKind.Compare, TemplateParts.Compare(CompareOperation.GreaterOrEqual))
            .Wire("position.barsSinceEntry", "timeUp.a")
            .Wire("params.maxHold", "timeUp.b");
    }

    /// <summary>Closes the position on the first of the signals that holds.</summary>
    private static GraphSketch LeaveOnAny(GraphSketch sketch, params string[] signals)
    {
        string any = signals[0];

        for (int index = 1; index < signals.Length; index++)
        {
            string id = $"leave{index}";
            sketch.Node(id, SimpleNodeKind.Or).Wire(any, $"{id}.a").Wire(signals[index], $"{id}.b");
            any = $"{id}.result";
        }

        return sketch.Node("exit", SimpleNodeKind.ClosePosition).Wire(any, "exit.when");
    }
}