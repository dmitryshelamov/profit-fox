using System.Globalization;
using System.Text.Json;

using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Simulation.Orders;
using ProfitFox.Engine.Simulation.Plans;
using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Tests.Unit.Engine.Simulation.Plans;

/// <summary>
///     Outcomes of plans without stop and target match <c>simulate</c> of <c>outcome.py</c> of the Python bench on its synthetic paths
///     (<c>Fixtures/trade_plan_outcomes.json</c>, built by <c>export_parity_fixture.py</c>). Synthetic data only.
/// </summary>
[TestFixture]
public class PythonParityTests
{
    private const decimal Tolerance = 1e-12m;

    private static readonly Dictionary<string, TradeStatus> Statuses = new Dictionary<string, TradeStatus>
    {
        ["ok"] = TradeStatus.Ok,
        ["coverage"] = TradeStatus.ExcludedCoverage,
        ["no_trade"] = TradeStatus.ExcludedNoTrade,
        ["funding"] = TradeStatus.ExcludedFunding
    };

    [Test]
    public void The_outcomes_match_the_python_bench()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement root = fixture.RootElement;
        var costs = new Costs(Number(root.GetProperty("taker_fee"))!.Value, Number(root.GetProperty("slippage"))!.Value);
        var horizon = TimeSpan.FromMinutes(root.GetProperty("horizon_minutes").GetInt32());
        var statuses = new HashSet<TradeStatus>();
        int compared = 0;

        foreach (JsonElement pathElement in root.GetProperty("paths").EnumerateArray())
        {
            MinutePath path = Path(pathElement);
            var entries = pathElement.GetProperty("entries").EnumerateArray().Select(entry => Time(entry)!.Value).ToList();
            foreach ((string side, PositionSide positionSide) in new[] { ("short", PositionSide.Short), ("long", PositionSide.Long) })
            {
                IReadOnlyList<TradeOutcome> outcomes = TradePlanOutcomes.Compute(path, new TradePlan(positionSide, horizon), costs, entries);
                var expected = pathElement.GetProperty("outcomes").GetProperty(side).EnumerateArray().ToList();
                for (int index = 0; index < entries.Count; index++)
                {
                    string where = $"{path.InstrumentId} {side} {entries[index]:O}";
                    Compare(outcomes[index], expected[index], where);
                    statuses.Add(outcomes[index].Status);
                    compared++;
                }
            }
        }

        Assert.That(compared, Is.GreaterThan(50));
        Assert.That(statuses, Is.EquivalentTo(Statuses.Values));
    }

    private static void Compare(TradeOutcome actual, JsonElement expected, string where)
    {
        Assert.That(actual.Status, Is.EqualTo(Statuses[expected.GetProperty("status").GetString()!]), where);
        Assert.Multiple(() =>
        {
            Assert.That(actual.EntryTime, Is.EqualTo(Time(expected.GetProperty("entry_time"))), $"{where}: entry time");
            Assert.That(actual.ExitTime, Is.EqualTo(Time(expected.GetProperty("exit_time"))), $"{where}: exit time");
            Close(actual.EntryPrice, expected, "entry_price", where, relative: true);
            Close(actual.ExitPrice, expected, "exit_price", where, relative: true);
            Close(actual.Gross, expected, "gross", where, relative: false);
            Close(actual.Slippage, expected, "slippage", where, relative: false);
            Close(actual.Fees, expected, "fees", where, relative: false);
            Close(actual.Funding, expected, "funding", where, relative: false);
            Close(actual.Net, expected, "net", where, relative: false);
        });
    }

    private static void Close(decimal? actual, JsonElement expected, string field, string where, bool relative)
    {
        decimal? value = Number(expected.GetProperty(field));
        if (value is null)
        {
            Assert.That(actual, Is.Null, $"{where}: {field}");
            return;
        }

        decimal tolerance = relative ? Tolerance * Math.Abs(value.Value) : Tolerance;
        Assert.That(actual, Is.EqualTo(value.Value).Within(tolerance), $"{where}: {field}");
    }

    private static MinutePath Path(JsonElement element)
    {
        string instrument = element.GetProperty("instrument").GetString()!;
        DateTimeOffset start = Time(element.GetProperty("start"))!.Value;
        var candles = element.GetProperty("minutes").EnumerateArray()
            .Select(row => new MinuteCandle(
                start.AddMinutes(row[0].GetInt32()),
                Number(row[1])!.Value,
                Number(row[2])!.Value,
                Number(row[3])!.Value,
                Number(row[4])!.Value,
                Volume: 1m))
            .ToList();
        var coverage = element.GetProperty("coverage").EnumerateArray()
            .Select(span => new TimeRange(Time(span[0])!.Value, Time(span[1])!.Value))
            .ToList();
        var payments = element.GetProperty("payments").EnumerateArray()
            .Select(payment => new FundingPayment(instrument, Time(payment[0])!.Value, Number(payment[1])!.Value))
            .ToList();
        JsonElement funding = element.GetProperty("funding_coverage");
        return new MinutePath(instrument, candles, coverage, payments, new TimeRange(Time(funding[0])!.Value, Time(funding[1])!.Value));
    }

    private static decimal? Number(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null ? null : decimal.Parse(element.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? Time(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null
            ? null
            : DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static string FixturePath()
    {
        return System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Engine", "Simulation", "Fixtures", "trade_plan_outcomes.json");
    }
}