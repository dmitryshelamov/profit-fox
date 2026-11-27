using System.Globalization;

using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 2.6 (design.md, «Обновление пакета меняет числа ATR или RSI»): the values of
///     <c>atr</c> and <c>rsi</c> with a length of 14 on 600 candles written in <c>Fixtures/Indicators/candles.csv</c>, as
///     <c>FacioQuo.Stock.Indicators</c> 3.1.0 gave them, are in <c>values.csv</c>. A new version of the package that changes
///     any of them fails here, and then the version of the engine goes up.
/// </summary>
public sealed class IndicatorFixtureTests
{
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Domain", "Services", "Nodes", "Fixtures", "Indicators");

    [Test]
    public void The_atr_and_the_rsi_of_the_written_candles_are_the_written_values()
    {
        List<ClosedCandle> candles = [.. Rows("candles.csv").Select(Candle)];
        List<string[]> values = [.. Rows("values.csv")];

        decimal?[] atr = NodeGraphs.Numbers(NodeGraphs.Graph("number", "n.result", """{ "id": "n", "kind": "atr", "inputs": { "length": 14 } }"""), candles);
        decimal?[] rsi = NodeGraphs.Numbers(NodeGraphs.OfClose("rsi", "value", "\"length\": 14"), candles);

        Assert.That(candles, Has.Count.EqualTo(600));
        Assert.That(values.Select(row => row[0]), Is.EqualTo(Rows("candles.csv").Select(row => row[0])));
        Assert.That(atr, Is.EqualTo(values.Select(row => Number(row[1]))));
        Assert.That(rsi, Is.EqualTo(values.Select(row => Number(row[2]))));
        Assert.That(atr.Count(value => value is not null), Is.EqualTo(586));
    }

    private static ClosedCandle Candle(string[] row)
    {
        var open = DateTimeOffset.Parse(row[0], CultureInfo.InvariantCulture);
        return new ClosedCandle(open, open.AddHours(1), Number(row[1])!.Value, Number(row[2])!.Value, Number(row[3])!.Value, Number(row[4])!.Value, Number(row[5])!.Value);
    }

    private static decimal? Number(string text)
    {
        return text.Length == 0 ? null : decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Path.Combine(Folder, file)).Skip(1).Select(line => line.Split(','));
    }
}
