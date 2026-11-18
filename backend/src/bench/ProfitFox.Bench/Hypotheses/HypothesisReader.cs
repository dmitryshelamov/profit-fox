using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Hypotheses;

/// <summary>
///     Reads and checks <c>bench/hypotheses/H&lt;NNNN&gt;-&lt;name&gt;/hypothesis.json</c> before any history is read
///     (spec hypothesis-bench). Every problem is reported at once; unknown fields are problems too.
/// </summary>
public static partial class HypothesisReader
{
    public const string FileName = "hypothesis.json";

    private static readonly string[] Fields =
        ["id", "name", "statement", "created", "timeframe", "block", "randomsPerEvent", "measure", "minEffect", "costs", "signal", "plan"];

    private static readonly string[] PlanFields = ["side", "horizon", "stop", "target"];
    private static readonly string[] CostFields = ["takerFee", "slippage", "funding"];

    private static readonly Dictionary<string, TimeSpan> Timeframes = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["4h"] = TimeSpan.FromHours(4),
        ["1d"] = TimeSpan.FromDays(1)
    };

    public static Hypothesis Read(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new HypothesisException(folder, [$"there is no {FileName}"]);
        }

        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Parse(folder, text);
    }

    /// <summary>
    ///     The hypothesis of the text of its file, in a folder named after it.
    /// </summary>
    public static Hypothesis Parse(string folder, string text)
    {
        List<string> errors = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new HypothesisException(folder, [$"not JSON: {exception.Message}"]);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new HypothesisException(folder, ["the file must be a JSON object"]);
            }

            Unknown(root, Fields, "", errors);
            string id = Text(root, "id", errors) ?? "";
            if (id.Length > 0 && !IdPattern().IsMatch(id))
            {
                errors.Add("id must be H followed by four digits");
            }
            else if (id.Length > 0 && !Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).StartsWith($"{id}-", StringComparison.Ordinal))
            {
                errors.Add($"the folder must be named {id}-<name>");
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
            if (Text(root, "timeframe", errors) is { } timeframeText && !Timeframes.TryGetValue(timeframeText, out timeframe))
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
            TradePlan? plan = Plan(root, errors);
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

            if (errors.Count > 0)
            {
                throw new HypothesisException(folder, errors);
            }

            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            return new Hypothesis(folder, id, name, statement, created, timeframe, block, randoms, measure, minEffect, costs, signal!, plan!, hash);
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

    private static TradePlan? Plan(JsonElement root, List<string> errors)
    {
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
