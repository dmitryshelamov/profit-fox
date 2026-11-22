using System.Globalization;

using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     The 1200 trades of the Python bench in <c>Fixtures/Outcomes</c> (see <see cref="OutcomeParityTests" />) with their
///     paths, read the way that test reads them, for the tests of change add-position-node that compare the calculation of
///     a cycle with the trade simulator.
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
