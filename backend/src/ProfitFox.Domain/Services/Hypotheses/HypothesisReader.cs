using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Hypotheses.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Hypotheses;

/// <summary>
///     Checks the text of <c>hypothesis.json</c> before any history is read
///     (spec hypothesis-bench). Every problem is reported at once; unknown fields are problems too. Reading the file is
///     the job of the hypothesis store.
/// </summary>
public static partial class HypothesisReader
{
    private static readonly string[] Fields =
        ["id", "name", "statement", "created", "timeframe", "block", "randomsPerEvent", "measure", "minEffect", "costs", "signal", "plan"];

    private static readonly string[] PlanFields = ["side", "horizon", "stop", "target"];
    private static readonly string[] CostFields = ["takerFee", "slippage", "funding"];

    /// <summary>
    ///     The text with <c>\n</c> line ends: the hash of a hypothesis is the hash of this text, so a file saved on Windows has
    ///     the hash of the same file saved elsewhere.
    /// </summary>
    public static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    ///     The hypothesis of a stored version: its text as that version, which must be a text of the stored id.
    /// </summary>
    /// <exception cref="HypothesisException">The text cannot be run, or it is the text of another hypothesis.</exception>
    public static Hypothesis Parse(StoredHypothesis stored)
    {
        Hypothesis hypothesis = Parse(stored.Text, stored.Version);
        return hypothesis.Id == stored.Id ? hypothesis : throw new HypothesisException(stored.Id, [$"the text is of {hypothesis.Id}"]);
    }

    /// <summary>
    ///     The hypothesis of the <paramref name="text" /> of its file, as version <paramref name="version" /> of its id.
    /// </summary>
    public static Hypothesis Parse(string text, int version = 1)
    {
        const string unknown = "hypothesis";
        List<string> errors = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new HypothesisException(unknown, [$"not JSON: {exception.Message}"]);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new HypothesisException(unknown, ["the file must be a JSON object"]);
            }

            Unknown(root, Fields, "", errors);
            string id = Text(root, "id", errors) ?? "";
            if (id.Length > 0 && !IdPattern().IsMatch(id))
            {
                errors.Add("id must be H followed by four digits");
            }

            string name = Text(root, "name", errors) ?? "";
            string statement = Text(root, "statement", errors) ?? "";
            DateOnly created = default;
            if (Text(root, "created", errors) is { } createdText
                && !DateOnly.TryParseExact(createdText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out created))
            {
                errors.Add("created must be a date yyyy-MM-dd");
            }

            TimeSpan timeframe = default;
            if (Text(root, "timeframe", errors) is { } timeframeText && !Timeframes.TryParse(timeframeText, out timeframe))
            {
                errors.Add("timeframe must be 1h, 4h or 1d");
            }

            TimeBlock block = TimeBlock.Week;
            if (Text(root, "block", errors) is { } blockText)
            {
                block = blockText switch
                {
                    "day" => TimeBlock.Day,
                    "week" => TimeBlock.Week,
                    _ => Error(errors, "block must be day or week", TimeBlock.Week)
                };
            }

            int randoms = Integer(root, "randomsPerEvent", errors) ?? 2;
            if (randoms < 2)
            {
                errors.Add("randomsPerEvent must be at least 2");
            }

            Measure measure = Measure.Net;
            if (Text(root, "measure", errors) is { } measureText)
            {
                measure = measureText switch
                {
                    "net" => Measure.Net,
                    "r" => Measure.R,
                    _ => Error(errors, "measure must be net or r", Measure.Net)
                };
            }

            double minEffect = Number(root, "minEffect", errors) ?? 0;
            if (!(minEffect > 0))
            {
                errors.Add("minEffect must be above 0");
            }

            TradeCosts costs = Costs(root, errors);
            TradePlan? plan = Plan(root, errors, out decimal? stop, out decimal? target);
            Signal? signal = SignalOf(root, errors);

            if (plan is not null)
            {
                if (measure == Measure.R && plan.Stop is null)
                {
                    errors.Add("measure r needs a stop in the plan");
                }

                if (Length(block) < plan.Horizon)
                {
                    errors.Add("the block must not be shorter than the horizon of the plan");
                }
            }

            TradingStrategy? strategy = errors.Count == 0 ? StrategyOf(signal!, plan!, stop, target, errors) : null;
            if (errors.Count > 0)
            {
                throw new HypothesisException(IdPattern().IsMatch(id) ? id : unknown, errors);
            }

            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            return new Hypothesis(id, version, name, statement, created, timeframe, block, randoms, measure, minEffect, costs, signal!, plan!, strategy!, hash);
        }
    }

    public static TimeSpan Length(TimeBlock block)
    {
        return block == TimeBlock.Day ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
    }

    private static TradeCosts Costs(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("costs", out JsonElement costs))
        {
            return TradeCosts.Default;
        }

        if (costs.ValueKind != JsonValueKind.Object)
        {
            errors.Add("costs must be an object");
            return TradeCosts.Default;
        }

        Unknown(costs, CostFields, "costs.", errors);
        double fee = costs.TryGetProperty("takerFee", out _) ? Number(costs, "takerFee", errors, "costs.") ?? 0 : TradeCosts.Default.TakerFee;
        double slippage = costs.TryGetProperty("slippage", out _) ? Number(costs, "slippage", errors, "costs.") ?? 0 : TradeCosts.Default.Slippage;
        bool funding = TradeCosts.Default.Funding;
        if (costs.TryGetProperty("funding", out JsonElement fundingElement))
        {
            if (fundingElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                funding = fundingElement.GetBoolean();
            }
            else
            {
                errors.Add("costs.funding must be true or false");
            }
        }

        if (fee < 0 || slippage < 0)
        {
            errors.Add("costs must not be negative");
        }

        return new TradeCosts(fee, slippage, funding);
    }

    // The plan as before, and its stop and target as the decimals of the text: they go into the graph of the strategy without
    // passing through double.
    private static TradePlan? Plan(JsonElement root, List<string> errors, out decimal? stopDecimal, out decimal? targetDecimal)
    {
        stopDecimal = null;
        targetDecimal = null;
        if (!root.TryGetProperty("plan", out JsonElement plan) || plan.ValueKind != JsonValueKind.Object)
        {
            errors.Add("plan must be an object");
            return null;
        }

        Unknown(plan, PlanFields, "plan.", errors);
        TradeSide? side = Text(plan, "side", errors, "plan.") switch
        {
            "long" => TradeSide.Long,
            "short" => TradeSide.Short,
            null => null,
            _ => Error<TradeSide?>(errors, "plan.side must be long or short", null)
        };
        TimeSpan? horizon = null;
        if (Text(plan, "horizon", errors, "plan.") is { } horizonText)
        {
            horizon = Durations.TryParse(horizonText, out TimeSpan value) ? value : Error<TimeSpan?>(errors, "plan.horizon must be a duration such as 4h", null);
        }

        double? stop = plan.TryGetProperty("stop", out _) ? Number(plan, "stop", errors, "plan.") : null;
        double? target = plan.TryGetProperty("target", out _) ? Number(plan, "target", errors, "plan.") : null;
        stopDecimal = Decimal(plan, "stop");
        targetDecimal = Decimal(plan, "target");
        if (side is null || horizon is null)
        {
            return null;
        }

        try
        {
            return new TradePlan(side.Value, horizon.Value, stop, target);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            errors.Add($"plan.{exception.ParamName}: {exception.Message.Split(" (Parameter")[0]}");
            return null;
        }
    }

    // The strategy a run executes (change add-strategy-graph, design.md, decision 8): the event of the signal enters on the
    // side of the plan and closes after its horizon, with its stop and target. The entries of the strategy must give the plan
    // of the text exactly; a number a decimal cannot hold would give another, so the hypothesis is refused.
    private static TradingStrategy? StrategyOf(Signal signal, TradePlan plan, decimal? stop, decimal? target, List<string> errors)
    {
        EntrySide side = plan.Side == TradeSide.Long ? EntrySide.Long : EntrySide.Short;
        var strategy = TradingStrategy.FromSignal(signal, side, plan.Horizon, stop, target);
        StrategyStep written = new StrategyExecutor(strategy).BeforeCandles(DateTimeOffset.UnixEpoch);
        if (EntryPlan.Of(written, side) != plan)
        {
            errors.Add("plan: stop and target must be decimal numbers a graph can hold, of at most 28 digits");
            return null;
        }

        return strategy;
    }

    private static decimal? Decimal(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)
            ? number
            : null;
    }

    private static Signal? SignalOf(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("signal", out JsonElement element))
        {
            errors.Add("signal is missing");
            return null;
        }

        ReadResult<Signal> signal = StrategyGraph.ReadSignal(element);
        errors.AddRange(signal.Errors.Select(error => $"signal: {error}"));
        return signal.Value;
    }

    private static void Unknown(JsonElement element, string[] known, string prefix, List<string> errors)
    {
        foreach (JsonProperty property in element.EnumerateObject().Where(property => !known.Contains(property.Name, StringComparer.Ordinal)))
        {
            errors.Add($"unknown field {prefix}{property.Name}");
        }
    }

    private static string? Text(JsonElement element, string name, List<string> errors, string prefix = "")
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{prefix}{name} is missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text)
        {
            errors.Add($"{prefix}{name} must be a non-empty string");
            return null;
        }

        return text;
    }

    private static double? Number(JsonElement element, string name, List<string> errors, string prefix = "")
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{prefix}{name} is missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"{prefix}{name} must be a number");
            return null;
        }

        return value.GetDouble();
    }

    private static int? Integer(JsonElement element, string name, List<string> errors)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{name} is missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            errors.Add($"{name} must be a whole number");
            return null;
        }

        return number;
    }

    private static T Error<T>(List<string> errors, string error, T fallback)
    {
        errors.Add(error);
        return fallback;
    }

    [GeneratedRegex("^H[0-9]{4}$")]
    private static partial Regex IdPattern();
}
