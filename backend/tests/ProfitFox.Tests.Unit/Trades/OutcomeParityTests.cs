using System.Globalization;

using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Trades;

/// <summary>
///     Task 5.2: the outcomes of the C# simulator equal those of <c>simulate</c> of the Python bench on the same synthetic
///     paths. The fixture (<c>Fixtures/Outcomes</c>, exported from the Python bench <c>profit-fox-bench</c>
///     at commit <c>13bd168</c> by a script removed by task 9.1, in commit <c>c1f09c0</c>) has four paths of 3000
///     minutes with gaps, minutes without trades, an uncovered stretch, partly known funding and a pair without funding,
///     and 1200 trades with their Python outcomes.
/// </summary>
public sealed class OutcomeParityTests
{
    private const double Tolerance = 1e-9;
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Trades", "Fixtures", "Outcomes");

    [Test]
    public void Every_outcome_equals_the_Python_one()
    {
        Dictionary<int, MinutePath> paths = Paths();
        List<string> differences = [];
        int count = 0;
        foreach (string[] row in Rows("trades.csv"))
        {
            count++;
            int path = int.Parse(row[0], CultureInfo.InvariantCulture);
            DateTimeOffset entry = Time(row[1]);
            var plan = new TradePlan(
                row[2] == "1" ? TradeSide.Long : TradeSide.Short,
                TimeSpan.FromMinutes(int.Parse(row[3], CultureInfo.InvariantCulture)),
                Optional(row[4]),
                Optional(row[5]));

            TradeOutcome outcome = TradeSimulator.Simulate(paths[path], entry, plan, TradeCosts.Default);

            string where = $"path {path} entry {entry:O} {plan.Side} {plan.HorizonMinutes}m stop {row[4]} target {row[5]}";
            Check(where, "status", (int)outcome.Status, int.Parse(row[6], CultureInfo.InvariantCulture), differences);
            Check(where, "exit", (int)outcome.ExitReason, int.Parse(row[7], CultureInfo.InvariantCulture), differences);
            Check(where, "ambiguous", outcome.Ambiguous, row[8] == "True", differences);
            Check(where, "entry time", outcome.EntryTime?.ToUnixTimeMilliseconds() ?? -1, long.Parse(row[9], CultureInfo.InvariantCulture), differences);
            Check(where, "exit time", outcome.ExitTime?.ToUnixTimeMilliseconds() ?? -1, long.Parse(row[10], CultureInfo.InvariantCulture), differences);
            double?[] actual = [outcome.EntryPrice, outcome.ExitPrice, outcome.Gross, outcome.Fees, outcome.Slippage, outcome.Funding, outcome.Net, outcome.R];
            string[] names = ["entry price", "exit price", "gross", "fees", "slippage", "funding", "net", "r"];
            for (int index = 0; index < names.Length; index++)
            {
                double expected = Number(row[11 + index]);
                double value = actual[index] ?? double.NaN;
                bool same = double.IsNaN(expected) ? double.IsNaN(value) : Math.Abs(value - expected) <= Tolerance;
                if (!same)
                {
                    differences.Add($"{where}: {names[index]} {value:R} != {expected:R}");
                }
            }
        }

        Assert.That(count, Is.EqualTo(1200));
        Assert.That(differences, Is.Empty);
    }

    private static Dictionary<int, MinutePath> Paths()
    {
        ILookup<int, string[]> minutes = Rows("minutes.csv").ToLookup(row => int.Parse(row[0], CultureInfo.InvariantCulture));
        ILookup<int, string[]> funding = Rows("funding.csv").ToLookup(row => int.Parse(row[0], CultureInfo.InvariantCulture));
        var paths = new Dictionary<int, MinutePath>();
        foreach (string[] row in Rows("paths.csv"))
        {
            int number = int.Parse(row[0], CultureInfo.InvariantCulture);
            string[][] rows = [.. minutes[number].OrderBy(minute => int.Parse(minute[1], CultureInfo.InvariantCulture))];
            paths[number] = new MinutePath(
                Time(row[1]),
                [.. rows.Select(minute => Number(minute[2]))],
                [.. rows.Select(minute => Number(minute[3]))],
                [.. rows.Select(minute => Number(minute[4]))],
                [.. rows.Select(minute => Number(minute[5]))],
                [.. rows.Select(minute => minute[6] == "1")],
                [.. funding[number].Select(payment => (Time(payment[1]), Number(payment[2])))],
                row[3].Length > 0 ? Time(row[3]) : null,
                row[4].Length > 0 ? Time(row[4]) : null);
        }

        return paths;
    }

    private static void Check<T>(string where, string name, T actual, T expected, List<string> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            differences.Add($"{where}: {name} {actual} != {expected}");
        }
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Path.Combine(Folder, file)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static DateTimeOffset Time(string milliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(milliseconds, CultureInfo.InvariantCulture));
    }

    private static double Number(string text)
    {
        return text == "NaN" ? double.NaN : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double? Optional(string text)
    {
        return text.Length == 0 ? null : Number(text);
    }
}
