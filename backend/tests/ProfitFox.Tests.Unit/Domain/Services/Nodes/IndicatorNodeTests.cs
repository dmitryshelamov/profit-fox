using System.Globalization;

using FacioQuo.Stock.Indicators;

using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 2.6: <c>atr</c> and <c>rsi</c> on the buffer lists of the package (spec
///     strategy-execution «Средний истинный диапазон», «RSI»): the numbers of the spec, the numbers of the batch
///     <c>ToAtr</c> and <c>ToRsi</c> on the same candles, and what <c>rsi</c> gives on a still price and on falls alone.
/// </summary>
public sealed class IndicatorNodeTests
{
    [Test]
    public void The_first_atr_is_the_mean_of_the_first_true_ranges()
    {
        decimal?[] atr = Atr(2, (10, 8, 9), (11, 9, 10), (13, 10, 12));

        Assert.That(atr, Is.EqualTo(new decimal?[] { null, null, 2.5m }));
    }

    [Test]
    public void The_next_atr_is_smoothed_by_wilder()
    {
        Assert.That(Atr(2, (10, 8, 9), (11, 9, 10), (13, 10, 12), (14, 12, 13))[3], Is.EqualTo(2.25m));
    }

    [Test]
    public void A_gap_counts_from_the_previous_close()
    {
        // The true ranges of the second and third candles are 4 — from the close of 100 to the high of 104 — and 2.
        Assert.That(Atr(2, (100, 99, 100), (104, 103, 103.5m), (105.5m, 103.5m, 104))[2], Is.EqualTo(3m));
    }

    /// <summary>
    ///     The package counts an ATR from a length of 2 (design.md, decision 3). A written 1 is an error of the graph (task 8.2,
    ///     «ATR с длиной один в графе»); a 1 from a parameter gives no value at any candle rather than a failed run («ATR с
    ///     длиной один из параметра»).
    /// </summary>
    [Test]
    public void Atr_with_a_length_of_one_from_a_parameter_has_no_value()
    {
        string json = NumberNodeTests.Lengthed("atr", length: null, """{ "name": "len", "type": "number", "value": 1 }""");
        ClosedCandle[] candles = [.. RandomCandles(new Random(1), 30)];

        Assert.That(NodeGraphs.Numbers(json, candles), Has.All.Null);
        Assert.That(NodeGraphs.Numbers(json.Replace("\"value\": 1 }", "\"value\": 2 }", StringComparison.Ordinal), candles).Last(), Is.Not.Null, "a length of 2 counts");
    }

    [Test]
    public void Atr_has_no_value_on_as_many_candles_as_its_length()
    {
        (decimal, decimal, decimal)[] candles = [.. Enumerable.Range(0, 15).Select(index => (100m + index, 98m + index, 99m + index))];

        decimal?[] atr = Atr(14, candles);

        Assert.That(atr.Take(14), Has.All.Null);
        Assert.That(atr[14], Is.Not.Null);
    }

    [Test]
    public void The_first_rsi()
    {
        Assert.That(Rsi(2, 10, 13, 12), Is.EqualTo(new decimal?[] { null, null, 75 }));
    }

    [Test]
    public void The_next_rsi_is_smoothed_by_wilder()
    {
        Assert.That(Rsi(2, 10, 13, 12, 14)[3], Is.EqualTo(87.5m));
    }

    [Test]
    public void Rsi_has_no_value_on_as_many_values_as_its_length()
    {
        decimal?[] rsi = Rsi(14, [.. Enumerable.Range(0, 15).Select(index => 100m + (index % 3))]);

        Assert.That(rsi.Take(14), Has.All.Null);
        Assert.That(rsi[14], Is.Not.Null);
    }

    /// <summary>
    ///     What the package gives where <c>G / L</c> has no value: a loss of 0 makes the RSI 100, a still price too, and a
    ///     gain of 0 makes it 0 (task 2.6 writes down the behaviour found).
    /// </summary>
    [Test]
    public void Rsi_on_a_still_price_is_a_hundred_and_on_falls_alone_zero()
    {
        Assert.That(Rsi(14, [.. Enumerable.Repeat(100m, 20)]).Last(), Is.EqualTo(100m));
        Assert.That(Rsi(14, [.. Enumerable.Range(0, 20).Select(index => 100m - index)]).Last(), Is.EqualTo(0m));
        Assert.That(Rsi(14, [.. Enumerable.Range(0, 20).Select(index => 100m + index)]).Last(), Is.EqualTo(100m));
    }

    // The node keeps two results of the package and gives each one as the batch does over the whole history.
    [TestCase(2)]
    [TestCase(14)]
    [TestCase(20)]
    [TestCase(100)]
    public void On_random_candles_atr_gives_the_last_value_of_the_batch_count(int length)
    {
        ClosedCandle[] candles = [.. RandomCandles(new Random(length), 3000)];

        decimal?[] atr = NodeGraphs.Numbers(NodeGraphs.Graph("number", "n.result", $$"""{ "id": "n", "kind": "atr", "inputs": { "length": {{length}} } }"""), candles);

        Assert.That(atr, Is.EqualTo(Bars(candles).ToAtr(length).Select(result => Decimal(result.Atr))));
    }

    [TestCase(1)]
    [TestCase(14)]
    [TestCase(20)]
    [TestCase(100)]
    public void On_random_candles_rsi_gives_the_last_value_of_the_batch_count(int length)
    {
        ClosedCandle[] candles = [.. RandomCandles(new Random(length), 3000)];

        decimal?[] rsi = NodeGraphs.Numbers(NodeGraphs.OfClose("rsi", "value", $"\"length\": {length}"), candles);

        Assert.That(rsi, Is.EqualTo(Bars(candles).ToRsi(length).Select(result => Decimal(result.Rsi))));
    }

    [Test]
    public void The_result_has_at_most_fifteen_significant_digits()
    {
        decimal?[] atr = Atr(3, (10, 8, 9), (11.1m, 9, 10), (13, 10.3m, 12), (14, 12, 13.7m), (14.9m, 13.1m, 14));

        Assert.That(atr[4], Is.Not.Null);
        Assert.That(atr[4]!.Value.ToString(CultureInfo.InvariantCulture).Replace(".", "", StringComparison.Ordinal).TrimStart('0').Length, Is.LessThanOrEqualTo(15));
    }

    internal static IEnumerable<ClosedCandle> RandomCandles(Random random, int count, decimal start = 100)
    {
        decimal price = start;
        for (int index = 0; index < count; index++)
        {
            decimal open = price;
            price = Math.Max(1, Math.Round(price * (1 + (decimal)((random.NextDouble() - 0.5) * 0.04)), 4));
            decimal high = Math.Round(Math.Max(open, price) * (1 + (decimal)(random.NextDouble() * 0.01)), 4);
            decimal low = Math.Round(Math.Min(open, price) * (1 - (decimal)(random.NextDouble() * 0.01)), 4);
            yield return NodeGraphs.Bar(index, open, high, low, price, random.Next(1, 10000));
        }
    }

    private static List<Bar> Bars(IEnumerable<ClosedCandle> candles)
    {
        return [.. candles.Select(candle => new Bar(candle.OpenTime.UtcDateTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume))];
    }

    private static decimal? Decimal(double? value)
    {
        return value is { } number && double.IsFinite(number) ? (decimal)number : null;
    }

    private static decimal?[] Atr(int length, params (decimal High, decimal Low, decimal Close)[] candles)
    {
        string json = NodeGraphs.Graph("number", "n.result", $$"""{ "id": "n", "kind": "atr", "inputs": { "length": {{length}} } }""");
        return NodeGraphs.Numbers(json, candles.Select((candle, index) => NodeGraphs.Bar(index, candle.Close, candle.High, candle.Low, candle.Close)));
    }

    private static decimal?[] Rsi(int length, params decimal[] values)
    {
        return NodeGraphs.Numbers(NodeGraphs.OfClose("rsi", "value", $"\"length\": {length}"), NodeGraphs.Closes(values));
    }
}
