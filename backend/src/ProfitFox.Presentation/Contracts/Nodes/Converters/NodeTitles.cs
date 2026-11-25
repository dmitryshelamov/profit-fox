using ProfitFox.Domain.Services.Nodes;

namespace ProfitFox.Presentation.Contracts.Nodes.Converters;

/// <summary>
///     The words of the constructor for each kind of the engine catalog: its title and its group in the palette. The
///     engine has no words of the interface, so they live here; a test fails on a kind without them. The position nodes
///     have theirs too, though the catalog of a signal does not offer them, and so has the <c>signal</c> node of a strategy.
/// </summary>
public static class NodeTitles
{
    public const string Data = "Данные";
    public const string Logic = "Логика";
    public const string Numbers = "Числа";
    public const string Windows = "Окна";
    public const string Indicators = "Индикаторы";
    public const string Graph = "Граф";
    public const string Position = "Позиция";
    public const string Signals = "Сигналы";

    private static readonly Dictionary<string, (string Title, string Group)> Titles = new(StringComparer.Ordinal)
    {
        [NodeCatalog.Parameters] = ("Параметры", Graph),
        [NodeCatalog.Outputs] = ("Выходы", Graph),
        [NodeCatalog.Candle] = ("Свеча", Data),
        [NodeCatalog.FundingRate] = ("Фандинг", Data),
        [NodeCatalog.PairCandle] = ("Свеча пары", Data),
        [NodeCatalog.Previous] = ("Предыдущее", Data),
        [NodeCatalog.Compare] = ("Сравнить", Logic),
        [NodeCatalog.And] = ("И", Logic),
        [NodeCatalog.Or] = ("Или", Logic),
        [NodeCatalog.Not] = ("Не", Logic),
        [NodeCatalog.FirstTime] = ("Впервые", Logic),
        [NodeCatalog.IsTrue] = ("Истинно", Logic),
        [NodeCatalog.Add] = ("Сложить", Numbers),
        [NodeCatalog.Subtract] = ("Вычесть", Numbers),
        [NodeCatalog.Multiply] = ("Умножить", Numbers),
        [NodeCatalog.Divide] = ("Разделить", Numbers),
        [NodeCatalog.Min] = ("Меньшее", Numbers),
        [NodeCatalog.Max] = ("Большее", Numbers),
        [NodeCatalog.Select] = ("Выбрать", Numbers),
        [NodeCatalog.Window] = ("Окно", Windows),
        [NodeCatalog.Percentile] = ("Перцентиль", Windows),
        [NodeCatalog.Hold] = ("Запомнить", Windows),
        [NodeCatalog.Atr] = ("ATR", Indicators),
        [NodeCatalog.Rsi] = ("RSI", Indicators),
        [NodeCatalog.Zones] = ("Зоны", Indicators),
        [NodeCatalog.Candles] = ("Срок в свечах", Data),
        [NodeCatalog.Position] = ("Позиция", Position),
        [NodeCatalog.Enter] = ("Вход", Position),
        [NodeCatalog.MarketOrder] = ("Рыночная заявка", Position),
        [NodeCatalog.LimitOrder] = ("Лимитная заявка", Position),
        [NodeCatalog.ClosePosition] = ("Закрыть позицию", Position),
        [NodeCatalog.StopLoss] = ("Стоп", Position),
        [NodeCatalog.TakeProfit] = ("Цель", Position),
        [NodeCatalog.CloseAfter] = ("Закрыть через", Position),
        [NodeCatalog.Signal] = ("Сигнал", Signals)
    };

    public static (string Title, string Group)? Of(string kind)
    {
        return Titles.TryGetValue(kind, out (string Title, string Group) words) ? words : null;
    }
}
