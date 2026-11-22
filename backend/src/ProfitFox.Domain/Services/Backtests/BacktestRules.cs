using System.Globalization;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The check of the settings of a backtest (spec strategy-backtest «Настройки прогона», «История прогона», «Пары
///     прогона», «Наборы значений»): a request with an error in any setting is refused whole, with every error at once. The
///     graph of the version gives the parameters, its kind the rules of the pairs and of the start, the catalog the pairs
///     that are listed, and the clock the start of the current UTC day.
/// </summary>
public static class BacktestRules
{
    /// <summary>
    ///     The timeframe of a request that names none.
    /// </summary>
    public static readonly TimeSpan DefaultTimeframe = TimeSpan.FromHours(4);

    // The forms of a date: 2026-06-01, or a moment of ISO 8601 such as 2026-06-01T00:00:00Z.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    /// <summary>
    ///     The settings of <paramref name="request" /> for the graph <paramref name="graph" /> of a strategy of
    ///     <paramref name="kind" />, or every error.
    /// </summary>
    /// <param name="request">What was asked.</param>
    /// <param name="graph">The graph of the version: its parameters are those the request may give values to.</param>
    /// <param name="kind">The kind of the strategy.</param>
    /// <param name="listed">The symbols of the listed linear USDT perpetuals.</param>
    /// <param name="now">The time of the request.</param>
    public static BacktestCheck Check(BacktestRequest request, GraphDefinition graph, StrategyKind kind, IReadOnlyCollection<string> listed, DateTimeOffset now)
    {
        List<BacktestError> errors = [];
        IReadOnlyList<ParameterValues> values = Values(request.Values, graph, errors);
        IReadOnlyList<TimeSpan> timeframes = TimeframesOf(request.Timeframes, errors);
        if (BacktestGrid.Count(values, timeframes.Count) > Backtest.MaxSets)
        {
            errors.Add(new BacktestError(BacktestError.TooManySets, $"наборов больше {Backtest.MaxSets}: уменьшите число значений или таймфреймов") { Field = "values" });
        }

        DateTimeOffset today = HistoryPeriod.Today(now);
        DateTimeOffset? end = Date(request.End, "end", errors) ?? (request.End is null ? today : null);
        if (end > today)
        {
            errors.Add(new BacktestError(BacktestError.EndAfterToday, $"конец истории не позже начала текущих суток UTC, {today:yyyy-MM-dd}") { Field = "end" });
        }

        DateTimeOffset? start = Date(request.Start, "start", errors);
        if (request.Start is null && end is { } last)
        {
            start = kind == StrategyKind.Bot ? last - TimeSpan.FromDays(7 * Backtest.BotWeeks) : HistoryPeriod.DataStart;
        }

        if (start is { } first && end is { } until && until <= first)
        {
            errors.Add(new BacktestError(BacktestError.EmptyHistory, "конец истории позже её начала") { Field = "end" });
        }

        IReadOnlyList<string>? pairs = Pairs(request.Pairs, kind, listed, errors);
        double taker = Fee(request.TakerFee, TradeCosts.Default.TakerFee, "takerFee", "ставка тейкера", errors);
        double maker = Fee(request.MakerFee, TradeCosts.Default.MakerFee, "makerFee", "ставка мейкера", errors);
        return errors.Count > 0 || start is null || end is null
            ? new BacktestCheck(Settings: null, errors)
            : new BacktestCheck(new BacktestSettings(values, timeframes, start.Value, end.Value, pairs, taker, maker), []);
    }

    // The values by parameter in the order of the graph; each value is checked alone by the checks of the parameters.
    private static IReadOnlyList<ParameterValues> Values(IReadOnlyDictionary<string, IReadOnlyList<string>>? asked, GraphDefinition graph, List<BacktestError> errors)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        foreach (string name in asked.Keys.Where(name => graph.Parameters.All(parameter => parameter.Name != name)))
        {
            errors.Add(new BacktestError("UnknownParameter", $"у стратегии нет параметра {name}") { Field = "values", Parameter = name });
        }

        List<ParameterValues> values = [];
        foreach (ParameterDefinition parameter in graph.Parameters)
        {
            if (!asked.TryGetValue(parameter.Name, out IReadOnlyList<string>? texts) || texts.Count == 0)
            {
                continue;
            }

            foreach (string text in texts)
            {
                List<GraphError> found = [];
                GraphDefinition changed = GraphParameters.With(graph, [new(parameter.Name, text)], found);
                found.AddRange(GraphParameters.Check(changed).Where(error => error.Parameter == parameter.Name));
                errors.AddRange(found.Select(error => BacktestError.Of(error) with { Field = "values", Reason = $"{parameter.Name} = {text}: {error.Reason}" }));
            }

            values.Add(new ParameterValues(parameter.Name, [.. texts]));
        }

        return values;
    }

    private static IReadOnlyList<TimeSpan> TimeframesOf(IReadOnlyList<string>? asked, List<BacktestError> errors)
    {
        if (asked is null || asked.Count == 0)
        {
            return [DefaultTimeframe];
        }

        List<TimeSpan> timeframes = [];
        foreach (string name in asked)
        {
            if (!Timeframes.TryParse(name, out TimeSpan timeframe))
            {
                errors.Add(new BacktestError(BacktestError.InvalidTimeframe, $"таймфрейм {name} не из 1h, 4h, 1d") { Field = "timeframes" });
            }
            else if (!timeframes.Contains(timeframe))
            {
                timeframes.Add(timeframe);
            }
        }

        return timeframes;
    }

    // A date of the request: the start of a UTC day, as 2026-06-01 or a moment at midnight UTC.
    private static DateTimeOffset? Date(string? text, string field, List<BacktestError> errors)
    {
        if (text is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset moment)
            || !HistoryPeriod.IsDayStart(moment))
        {
            errors.Add(new BacktestError(BacktestError.InvalidDate, $"{text} — не начало суток UTC: дата пишется как 2026-06-01") { Field = field });
            return null;
        }

        return moment;
    }

    private static IReadOnlyList<string>? Pairs(IReadOnlyList<string>? asked, StrategyKind kind, IReadOnlyCollection<string> listed, List<BacktestError> errors)
    {
        string[] pairs = asked is null ? [] : [.. asked.Distinct(StringComparer.Ordinal)];
        if (pairs.Length == 0)
        {
            if (kind == StrategyKind.Bot)
            {
                errors.Add(new BacktestError(BacktestError.PairsRequired, "стратегии вида «для бота» нужен список пар") { Field = "pairs" });
            }

            return null;
        }

        if (kind == StrategyKind.Bot && pairs.Length > Backtest.MaxBotPairs)
        {
            errors.Add(new BacktestError(BacktestError.TooManyPairs, $"у стратегии вида «для бота» не больше {Backtest.MaxBotPairs} пар") { Field = "pairs" });
        }

        var known = new HashSet<string>(listed, StringComparer.Ordinal);
        foreach (string pair in pairs.Where(pair => !known.Contains(pair)))
        {
            errors.Add(new BacktestError(BacktestError.UnknownInstrument, $"{pair} нет среди торгуемых бессрочных USDT-пар") { Field = "pairs", Pair = pair });
        }

        return pairs;
    }

    // A fee: a fraction from 0 to below 1, or the rate of the bench when not given.
    private static double Fee(double? fee, double rate, string field, string name, List<BacktestError> errors)
    {
        if (fee is not { } value)
        {
            return rate;
        }

        if (value is >= 0 and < 1)
        {
            return value;
        }

        errors.Add(new BacktestError(BacktestError.InvalidFee, $"{name} — доля суммы исполнения, не меньше 0 и меньше 1") { Field = field });
        return rate;
    }
}
