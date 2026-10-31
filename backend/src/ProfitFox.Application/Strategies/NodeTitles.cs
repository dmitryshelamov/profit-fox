using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>Names of nodes as the editor shows them.</summary>
internal static class NodeTitles
{
    private static readonly Dictionary<SimpleNodeKind, string> Simple = new Dictionary<SimpleNodeKind, string>
    {
        [SimpleNodeKind.Parameters] = "Параметры",
        [SimpleNodeKind.Inputs] = "Входы узла",
        [SimpleNodeKind.Outputs] = "Выходы узла",
        [SimpleNodeKind.Candle] = "Свеча",
        [SimpleNodeKind.Position] = "Позиция",
        [SimpleNodeKind.Ema] = "EMA",
        [SimpleNodeKind.Cross] = "Пересечение",
        [SimpleNodeKind.Add] = "Сложить",
        [SimpleNodeKind.Subtract] = "Вычесть",
        [SimpleNodeKind.Multiply] = "Умножить",
        [SimpleNodeKind.Divide] = "Разделить",
        [SimpleNodeKind.Compare] = "Сравнить",
        [SimpleNodeKind.And] = "И",
        [SimpleNodeKind.Or] = "ИЛИ",
        [SimpleNodeKind.Not] = "НЕ",
        [SimpleNodeKind.MarketOrder] = "Рыночный ордер",
        [SimpleNodeKind.ClosePosition] = "Закрыть позицию",
        [SimpleNodeKind.HoldLimit] = "Держать лимитку"
    };

    public static string Of(SimpleNodeKind kind)
    {
        return Simple[kind];
    }

    public static string Of(CompositeNodeId id, StrategyLibrary library)
    {
        return id is BuiltInNodeId builtIn
            ? BuiltInComposites.All.Single(composite => composite.Node == builtIn.Node).Title
            : library.Composites.First(composite => composite.Id == ((CustomNodeId)id).Value).Name;
    }
}