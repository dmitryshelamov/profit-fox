using System.Globalization;

using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     The 1200 trades of the Python bench in <c>Fixtures/Outcomes</c> (see <see cref="OutcomeParityTests" />) with their
///     paths, read the way that test reads them, for the tests of change add-position-node that take a trade of a plan
///     from them.
/// </summary>
internal static class OutcomeFixture
{
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Domain", "Services", "Trades", "Fixtures", "Outcomes");

    public static IEnumerable<(MinutePath Path, DateTimeOffset Entry, TradePlan Plan)> Trades()
    {
        Dictionary<int, MinutePath> paths = Paths();
        foreach (string[] row in Rows("trades.csv"))
        {
            var plan = new TradePlan(
                row[2] == "1" ? TradeSide.Long : TradeSide.Short,
                TimeSpan.FromMinutes(int.Parse(row[3], CultureInfo.InvariantCulture)),
                Optional(row[4]),
                Optional(row[5]));
            yield return (paths[int.Parse(row[0], CultureInfo.InvariantCulture)], Time(row[1]), plan);
        }
    }

    /// <summary>
    ///     The trades with the minutes and payments of their paths as rows (change add-ideas-and-market, task 7.3): the open
    ///     time, prices and coverage of each minute, and each payment with its time and rate.
    /// </summary>
    public static IEnumerable<(MinutePath Path, DateTimeOffset Entry, TradePlan Plan, IReadOnlyList<(DateTimeOffset Open, double O, double H, double L, double C, bool Covered)> Minutes, IReadOnlyList<(DateTimeOffset Time, double Rate)> Payments)> TradesWithRows()
    {
        ILookup<int, string[]> minutes = Rows("minutes.csv").ToLookup(row => int.Parse(row[0], CultureInfo.InvariantCulture));
        ILookup<int, string[]> funding = Rows("funding.csv").ToLookup(row => int.Parse(row[0], CultureInfo.InvariantCulture));
        var starts = Rows("paths.csv").ToDictionary(row => int.Parse(row[0], CultureInfo.InvariantCulture), row => Time(row[1]));
        Dictionary<int, MinutePath> paths = Paths();
        foreach (string[] row in Rows("trades.csv"))
        {
            int number = int.Parse(row[0], CultureInfo.InvariantCulture);
            var plan = new TradePlan(
                row[2] == "1" ? TradeSide.Long : TradeSide.Short,
                TimeSpan.FromMinutes(int.Parse(row[3], CultureInfo.InvariantCulture)),
                Optional(row[4]),
                Optional(row[5]));
            (DateTimeOffset, double, double, double, double, bool)[] rows =
            [
                .. minutes[number]
                    .Select(minute => (starts[number].AddMinutes(int.Parse(minute[1], CultureInfo.InvariantCulture)), Number(minute[2]), Number(minute[3]), Number(minute[4]), Number(minute[5]), minute[6] == "1"))
                    .OrderBy(minute => minute.Item1)
            ];
            (DateTimeOffset, double)[] payments = [.. funding[number].Select(payment => (Time(payment[1]), Number(payment[2]))).OrderBy(payment => payment.Item1)];
            yield return (paths[number], Time(row[1]), plan, rows, payments);
        }
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
