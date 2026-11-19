using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Bench.Presentation.Nodes;

/// <summary>
///     The words of the constructor for each kind of the catalog: its title and its group in the palette. The engine has
///     no words of the interface, so they live here; a test fails on a kind without them.
/// </summary>
public static class NodeTitles
{
    public const string Data = "Данные";
    public const string Logic = "Логика";
    public const string Graph = "Граф";

    private static readonly Dictionary<string, (string Title, string Group)> Titles = new(StringComparer.Ordinal)
    {
        [NodeCatalog.Parameters] = ("Параметры", Graph),
        [NodeCatalog.Outputs] = ("Выходы", Graph),
        [NodeCatalog.Candle] = ("Свеча", Data),
        [NodeCatalog.FundingRate] = ("Фандинг", Data),
        [NodeCatalog.Previous] = ("Предыдущее", Data),
        [NodeCatalog.Compare] = ("Сравнить", Logic),
        [NodeCatalog.And] = ("И", Logic),
        [NodeCatalog.Or] = ("Или", Logic),
        [NodeCatalog.Not] = ("Не", Logic),
        [NodeCatalog.FirstTime] = ("Впервые", Logic)
    };

    public static (string Title, string Group)? Of(string kind)
    {
        return Titles.TryGetValue(kind, out (string Title, string Group) words) ? words : null;
    }
}
