using System.Globalization;
using System.Text;

using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Trades;
using ProfitFox.Domain.Verdicts;

namespace ProfitFox.Application.Runs;

/// <summary>
///     The markdown report of a run (spec hypothesis-bench); the ledger keeps it with the line of the run. The outcomes of
///     events appear only when the verdict read them. The report shows how many events were skipped because the position
///     of their pair was open: they have no rows, so they are in none of its tables.
/// </summary>
public static class RunReport
{
    public const string SurvivorsWarning =
        "> **Выжившие пары.** Стенд видит только пары, которые торгуются сейчас: снятые с торгов в данные не попали. "
        + "Результаты завышены для лонгов и «покупки просадки» и занижены для шортов слабых пар, тем сильнее, чем дальше в "
        + "прошлое. Гипотезу про слабые или перегретые пары нельзя считать подтверждённой без проверки на новых данных.";

    public static string Text(
        RunDesign design,
        LedgerEntry entry,
        Verdict? verdict,
        IReadOnlyList<EventRow> rows,
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo,
        IReadOnlyList<(long Block, double Mean, int Events)>? blocks,
        int pairs,
        int pairsWithEvents,
        int skipped)
    {
        var text = new StringBuilder();
        Line(text, $"# {design.Hypothesis.Id} {design.Hypothesis.Name}: {entry.Period}");
        Line(text);
        Line(text, $"{SurvivorsWarning}");
        Line(text);
        Line(text, $"{design.Hypothesis.Statement}");
        Line(text);
        Line(text, $"| | |");
        Line(text, $"|---|---|");
        Line(text, $"| Период | {design.PeriodStart:yyyy-MM-dd} — {design.PeriodEnd.AddDays(-1):yyyy-MM-dd} |");
        Line(text, $"| Прогон | `{entry.RunId}`, {entry.Time:yyyy-MM-dd HH:mm} UTC |");
        Line(text, $"| Версия гипотезы | {design.Hypothesis.Version} |");
        Line(text, $"| Версия движка | {entry.EngineVersion} |");
        Line(text, $"| Хэш гипотезы | `{entry.Hash}` |");
        Line(text, $"| Хэш данных | `{entry.DataHash}` |");
        Line(text, $"| Пар в наборе / с событиями | {pairs} / {pairsWithEvents} |");
        Line(text, $"| Событий пропущено при открытой позиции | {skipped} |");
        Line(text, $"| Чтение истории и расчёт | {entry.ReadSeconds:F0} с |");
        Line(text);

        Line(text, $"## Вердикт");
        Line(text);
        Line(text, $"**{Name(entry.Status)}**: {entry.Reason}.");
        Line(text);
        Line(text, $"| Попыток | Нужный t | t | Мощность | Блоков | Событий |");
        Line(text, $"|---|---|---|---|---|---|");
        Line(text, $"| {entry.Attempts} | {entry.RequiredT:F2} | {(entry.T is { } t ? t.ToString("F2", CultureInfo.InvariantCulture) : "—")} | {entry.Power:F2} | {entry.Blocks} | {entry.Events} |");
        Line(text);

        Line(text, $"## Исключённые сделки");
        Line(text);
        Line(text, $"| Причина | События | Случайные входы |");
        Line(text, $"|---|---|---|");
        foreach (TradeStatus status in Enum.GetValues<TradeStatus>())
        {
            int events = rows.Count(row => row.Status == status);
            int randoms = rows.Sum(row => row.Randoms.Count(trade => trade.Outcome.Status == status));
            Line(text, $"| {Status(status)} | {events} | {randoms} |");
        }

        Line(text, $"| нет случайных входов в блоке | {rows.Count(row => row.Randoms.Count == 0)} | |");
        Line(text);

        bool outcomesRead = verdict is { T: not null } && blocks is not null;
        if (!outcomesRead)
        {
            Line(text, $"Исходы событий не читались: {(entry.Status == RunStatus.Parity ? "сверочный прогон." : "мощности или блоков мало.")}");
            Line(text);
            Line(text, $"## «Ложные события» за блоки");
            Line(text);
            Table(text, pseudo, design);
            return text.ToString();
        }

        EventRow[] usable = [.. rows.Where(row => row.Usable)];
        Line(text, $"## Средние по учтённым событиям, доли суммы сделки");
        Line(text);
        Line(text, $"| | Мера | До издержек | Комиссии | Проскальзывание | Фандинг | После издержек |");
        Line(text, $"|---|---|---|---|---|---|---|");
        Fields(text, "События", TradeFields.Mean([.. usable.Select(row => row.Event!)]));
        Fields(text, "Случайные входы", TradeFields.Mean([.. usable.Select(row => row.RandomMean!)]));
        Line(text);
        Line(text, $"## «Событие минус случайные входы» за блоки");
        Line(text);
        Table(text, blocks!, design);
        return text.ToString();
    }

    private static void Fields(StringBuilder text, string name, TradeFields? fields)
    {
        if (fields is null)
        {
            Line(text, $"| {name} | — | — | — | — | — | — |");
            return;
        }

        Line(text, $"| {name} | {fields.Y:F5} | {fields.Gross:F5} | {fields.Fees:F5} | {fields.Slippage:F5} | {fields.Funding:F5} | {fields.Net:F5} |");
    }

    private static void Table(StringBuilder text, IReadOnlyList<(long Block, double Mean, int Events)> blocks, RunDesign design)
    {
        Line(text, $"| Блок | Событий | Среднее |");
        Line(text, $"|---|---|---|");
        foreach ((long block, double mean, int events) in blocks)
        {
            Line(text, $"| {Blocks.Start(block, design.Hypothesis.Block):yyyy-MM-dd} | {events} | {mean:F5} |");
        }
    }

    private static string Name(string status)
    {
        return status switch
        {
            RunStatus.Passed => "Прошла",
            RunStatus.Rejected => "Отвергнута",
            RunStatus.InsufficientData => "Данных мало",
            RunStatus.Parity => "Сверка",
            _ => status
        };
    }

    private static string Status(TradeStatus status)
    {
        return status switch
        {
            TradeStatus.Ok => "учтены",
            TradeStatus.Coverage => "вне покрытия минут (`coverage`)",
            TradeStatus.NoTrade => "нет сделок в сроке (`no_trade`)",
            TradeStatus.Funding => "вне покрытия фандинга (`funding`)",
            _ => "недопустимая заявка (`plan`)"
        };
    }

    private static void Line(StringBuilder text, FormattableString? line = null)
    {
        text.Append(line is null ? "" : line.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }
}
