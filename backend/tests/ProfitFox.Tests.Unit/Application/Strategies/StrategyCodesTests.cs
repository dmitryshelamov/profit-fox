using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Templates;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Strategies;

public class StrategyCodesTests
{
    private static IEnumerable<TestCaseData> Codes()
    {
        (string, Enum)[] pairs =
        [
            ("parameters", SimpleNodeKind.Parameters), ("inputs", SimpleNodeKind.Inputs), ("outputs", SimpleNodeKind.Outputs),
            ("candle", SimpleNodeKind.Candle), ("position", SimpleNodeKind.Position), ("ema", SimpleNodeKind.Ema),
            ("sma", SimpleNodeKind.Sma),
            ("std-dev", SimpleNodeKind.StdDev),
            ("rsi", SimpleNodeKind.Rsi),
            ("previous", SimpleNodeKind.Previous),
            ("cross", SimpleNodeKind.Cross), ("add", SimpleNodeKind.Add), ("subtract", SimpleNodeKind.Subtract),
            ("multiply", SimpleNodeKind.Multiply), ("divide", SimpleNodeKind.Divide), ("compare", SimpleNodeKind.Compare),
            ("and", SimpleNodeKind.And), ("or", SimpleNodeKind.Or), ("not", SimpleNodeKind.Not),
            ("market-order", SimpleNodeKind.MarketOrder), ("close-position", SimpleNodeKind.ClosePosition), ("hold-limit", SimpleNodeKind.HoldLimit),
            ("number", PinType.Number), ("boolean", PinType.Boolean), ("duration", PinType.Duration),
            ("long", StrategyDirection.Long), ("short", StrategyDirection.Short),
            ("buy", OrderSide.Buy), ("sell", OrderSide.Sell),
            ("quantity", OrderSizeMode.Quantity), ("amount", OrderSizeMode.Amount),
            ("gt", CompareOperation.Greater), ("ge", CompareOperation.GreaterOrEqual), ("lt", CompareOperation.Less), ("le", CompareOperation.LessOrEqual),
            ("ema-cross", BuiltInNode.EmaCross), ("macd", BuiltInNode.Macd), ("bollinger", BuiltInNode.Bollinger),
            ("strategy", UsageKind.Strategy), ("node", UsageKind.Composite),
            ("market", OrderType.Market), ("limit", OrderType.Limit),
            ("ema-crossover", StrategyTemplateKind.EmaCrossover), ("ema-short", StrategyTemplateKind.EmaShort), ("sma-crossover", StrategyTemplateKind.SmaCrossover),
            ("ladder", StrategyTemplateKind.Ladder), ("ladder-sma", StrategyTemplateKind.LadderSma), ("ladder-plus", StrategyTemplateKind.LadderPlus),
            ("dca", StrategyTemplateKind.Dca), ("macd-trend", StrategyTemplateKind.MacdTrend), ("rsi-reversion", StrategyTemplateKind.RsiReversion),
            ("bollinger-reversion", StrategyTemplateKind.BollingerReversion), ("capitulation-buyback", StrategyTemplateKind.CapitulationBuyback),
            ("volume-impulse", StrategyTemplateKind.VolumeImpulse), ("level-trade", StrategyTemplateKind.LevelTrade),
            ("level-trade-short", StrategyTemplateKind.LevelTradeShort)
        ];

        return pairs.Select(pair => new TestCaseData(pair.Item1, pair.Item2).SetArgDisplayNames(pair.Item1));
    }

    [TestCaseSource(nameof(Codes))]
    public void Code_maps_to_its_value_and_back(string code, Enum value)
    {
        StrategyCodes.ToCode(value).ShouldBe(code);
        StrategyCodes.TryParse(value.GetType(), code, out Enum? parsed).ShouldBeTrue();
        parsed.ShouldBe(value);
    }

    [Test]
    public void Every_value_of_every_coded_enum_has_a_code()
    {
        Type[] coded = [typeof(SimpleNodeKind), typeof(PinType), typeof(StrategyDirection), typeof(OrderSide), typeof(OrderSizeMode), typeof(CompareOperation), typeof(BuiltInNode), typeof(UsageKind), typeof(OrderType), typeof(StrategyTemplateKind)];

        coded.SelectMany(type => Enum.GetValues(type).Cast<Enum>()).Count().ShouldBe(Codes().Count());
    }

    // Scenario "Ladder и DCA не встроены".
    [TestCase("ladder")]
    [TestCase("dca")]
    public void Ladder_and_dca_are_not_built_in_node_codes(string code)
    {
        StrategyCodes.TryParse(typeof(BuiltInNode), code, out _).ShouldBeFalse();
    }

    [TestCase("wma")]
    [TestCase("Ema")]
    [TestCase("")]
    public void Unknown_code_is_rejected(string code)
    {
        StrategyCodes.TryParse(typeof(SimpleNodeKind), code, out Enum? parsed).ShouldBeFalse();
        parsed.ShouldBeNull();
    }
}