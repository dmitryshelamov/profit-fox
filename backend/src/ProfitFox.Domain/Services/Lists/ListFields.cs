using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Lists;

/// <summary>
///     The fields of the lists and of the filters (change add-ideas-and-market, design.md, decisions 10 and 11). A list is
///     sorted, and a filter conditions, only by a field named here; a name outside is refused. Which column of the store a
///     field is lies with the store, by the same names.
/// </summary>
public static class ListFields
{
    public const string Long = "long";
    public const string Short = "short";

    /// <summary>
    ///     The score of an idea (change add-idea-score, design.md, decision 2): the sum of the weights of its labels by its side
    ///     now; a whole number in a condition.
    /// </summary>
    public const string Score = "score";

    /// <summary>
    ///     The sizes a page may have.
    /// </summary>
    public static readonly IReadOnlyList<int> PageSizes = [50, 100, 200];

    /// <summary>
    ///     The columns of «Рынок»; it sorts by the turnover of 24 hours by default.
    /// </summary>
    public static readonly IReadOnlyList<ListField> Market =
    [
        new ListField("symbol", "Пара", FieldType.Text, FieldPlace.Market),
        new ListField("price", "Цена", FieldType.Number, FieldPlace.Market),
        new ListField("change1h", "1 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("change4h", "4 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("change24h", "24 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("turnover24h", "Оборот 24 ч", FieldType.Number, FieldPlace.Market, ListField.Millions),
        new ListField("openInterest", "OI", FieldType.Number, FieldPlace.Market, ListField.Millions),
        new ListField("fundingRate", "Фандинг", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("ideas", "Идеи", FieldType.Number, FieldPlace.Market)
    ];

    /// <summary>
    ///     The columns of the lists of ideas, open and closed, and of the ideas of a pair; they sort by the moment by default.
    /// </summary>
    public static readonly IReadOnlyList<ListField> Ideas =
    [
        new ListField("symbol", "Пара", FieldType.Text, FieldPlace.Idea),
        new ListField("strategy", "Стратегия", FieldType.Text, FieldPlace.Idea),
        new ListField("timeframe", "ТФ", FieldType.Choice, FieldPlace.Idea, Values: Timeframes.All),
        new ListField("side", "Сторона", FieldType.Choice, FieldPlace.Idea, Values: [Long, Short]),
        new ListField("labels", "Метки", FieldType.Text, FieldPlace.Idea),
        new ListField(Score, "Балл", FieldType.Number, FieldPlace.Idea),
        new ListField("moment", "Появилась", FieldType.Time, FieldPlace.Idea),
        new ListField("entry", "Вход", FieldType.Number, FieldPlace.Idea),
        new ListField("stop", "Стоп", FieldType.Number, FieldPlace.Idea),
        new ListField("target", "Цель", FieldType.Number, FieldPlace.Idea),
        new ListField("left", "Осталось", FieldType.Number, FieldPlace.Idea),
        new ListField("now", "Сейчас", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("exitTime", "Выход", FieldType.Time, FieldPlace.Idea),
        new ListField("exitReason", "Чем закрыта", FieldType.Choice, FieldPlace.Idea, Values: ["stop", "target", "time"]),
        new ListField("result", "Итог", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("state", "Статус", FieldType.Choice, FieldPlace.Idea, Values: ["open", "closed", "excluded"])
    ];

    /// <summary>
    ///     The columns of the lists of trades (change take-idea-trades, design.md, decision 13): the open ones sort by the time
    ///     of the click from the newest, the closed ones by the exit.
    /// </summary>
    public static readonly IReadOnlyList<ListField> Trades =
    [
        new ListField("symbol", "Пара", FieldType.Text, FieldPlace.Idea),
        new ListField("account", "Аккаунт", FieldType.Text, FieldPlace.Idea),
        new ListField("strategy", "Идея", FieldType.Text, FieldPlace.Idea),
        new ListField("side", "Сторона", FieldType.Choice, FieldPlace.Idea, Values: [Long, Short]),
        new ListField("amount", "Сумма", FieldType.Number, FieldPlace.Idea),
        new ListField("created", "Клик", FieldType.Time, FieldPlace.Idea),
        new ListField("entryTime", "Вход", FieldType.Time, FieldPlace.Idea),
        new ListField("entry", "Цена входа", FieldType.Number, FieldPlace.Idea),
        new ListField("stop", "Стоп", FieldType.Number, FieldPlace.Idea),
        new ListField("target", "Цель", FieldType.Number, FieldPlace.Idea),
        new ListField("left", "До срока", FieldType.Time, FieldPlace.Idea),
        new ListField("now", "Сейчас", FieldType.Number, FieldPlace.Idea),
        new ListField("state", "Состояние", FieldType.Choice, FieldPlace.Idea, Values: ["pending", "open", "closing", "closed", "cancelled", "error"]),
        new ListField("exitTime", "Выход", FieldType.Time, FieldPlace.Idea),
        new ListField("exitReason", "Чем закрыта", FieldType.Text, FieldPlace.Idea),
        new ListField("fees", "Комиссии", FieldType.Number, FieldPlace.Idea),
        new ListField("result", "Итог, USDT", FieldType.Number, FieldPlace.Idea),
        new ListField("share", "Итог", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("ideaResult", "Итог идеи", FieldType.Number, FieldPlace.Idea, ListField.Percent)
    ];

    /// <summary>
    ///     The columns of the summary of the journal.
    /// </summary>
    public static readonly IReadOnlyList<ListField> Summary =
    [
        new ListField("strategy", "Стратегия", FieldType.Text, FieldPlace.Idea),
        new ListField("timeframe", "ТФ", FieldType.Choice, FieldPlace.Idea, Values: Timeframes.All),
        new ListField("ideas", "Идей", FieldType.Number, FieldPlace.Idea),
        new ListField("wins", "Прибыльных", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("mean", "Средний итог", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("sum", "Сумма, USDT", FieldType.Number, FieldPlace.Idea)
    ];

    /// <summary>
    ///     The fields of a filter (spec saved-filters «Поля фильтра»): those of the market and those of an idea.
    /// </summary>
    public static readonly IReadOnlyList<ListField> Filter =
    [
        new ListField("price", "Цена", FieldType.Number, FieldPlace.Market),
        new ListField("change1h", "Изменение 1 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("change4h", "Изменение 4 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("change24h", "Изменение 24 ч", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("turnover24h", "Оборот 24 ч", FieldType.Number, FieldPlace.Market, ListField.Millions),
        new ListField("openInterest", "OI", FieldType.Number, FieldPlace.Market, ListField.Millions),
        new ListField("fundingRate", "Фандинг", FieldType.Number, FieldPlace.Market, ListField.Percent),
        new ListField("openIdeas", "Открытых идей", FieldType.Number, FieldPlace.Market),
        new ListField("strategy", "Стратегия", FieldType.Text, FieldPlace.Idea),
        new ListField("timeframe", "Таймфрейм", FieldType.Choice, FieldPlace.Idea, Values: Timeframes.All),
        new ListField("side", "Сторона", FieldType.Choice, FieldPlace.Idea, Values: [Long, Short]),
        new ListField("label", "Метка", FieldType.Text, FieldPlace.Idea),
        new ListField(Score, "Балл", FieldType.Number, FieldPlace.Idea),
        new ListField("result", "Результат", FieldType.Number, FieldPlace.Idea, ListField.Percent),
        new ListField("minutesLeft", "Минут до срока", FieldType.Number, FieldPlace.Idea),
        new ListField("hasStop", "Есть стоп", FieldType.Boolean, FieldPlace.Idea, Values: ["true", "false"])
    ];

    /// <summary>
    ///     The page asked for, or the problems of the request: a field of sorting outside <paramref name="fields" />, a direction
    ///     other than <c>asc</c> or <c>desc</c>, a page below 1 or a size not of <see cref="PageSizes" />. Without a field the
    ///     list sorts by <paramref name="defaultSort" />, without a direction a number or a moment descends and a text ascends
    ///     (spec bench-ui «Сортировка по колонке»).
    /// </summary>
    public static ListOrder? Order(
        IReadOnlyList<ListField> fields,
        string defaultSort,
        string? sort,
        string? direction,
        int? page,
        int? pageSize,
        List<FilterProblem> problems)
    {
        int before = problems.Count;
        string name = string.IsNullOrEmpty(sort) ? defaultSort : sort;
        ListField? field = fields.FirstOrDefault(candidate => candidate.Name == name);
        if (field is null)
        {
            problems.Add(new FilterProblem("UnknownField", $"по полю {name} список не сортируется") { Field = "sort" });
        }

        bool descending = field?.Type is FieldType.Number or FieldType.Time;
        if (direction is "asc" or "desc")
        {
            descending = direction == "desc";
        }
        else if (!string.IsNullOrEmpty(direction))
        {
            problems.Add(new FilterProblem("InvalidDirection", "направление — asc или desc") { Field = "direction" });
        }

        if (page is < 1)
        {
            problems.Add(new FilterProblem("InvalidPage", "страница — с 1") { Field = "page" });
        }

        if (pageSize is { } size && !PageSizes.Contains(size))
        {
            problems.Add(new FilterProblem("InvalidPageSize", "на странице 50, 100 или 200 строк") { Field = "pageSize" });
        }

        return problems.Count > before ? null : new ListOrder(name, descending, page ?? 1, pageSize ?? PageSizes[0]);
    }
}
