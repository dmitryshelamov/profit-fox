using System.Globalization;

using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Tests.Unit.Engine.Strategy;

/// <summary>
///     The parity fixture of H0001 (<c>Fixtures/H0001</c>, built by <c>export_parity_fixture.py</c>): real funding payments of the
///     exploration period, the 4h candles <c>event.py</c> keeps of a generated grid, and the events <c>event.py</c> gave on them.
/// </summary>
internal sealed class ParityFixture
{
    /// <summary>
    ///     The candles in the order of the stream: by open time, then instrument.
    /// </summary>
    public IReadOnlyList<ClosedCandle> Candles { get; }

    /// <summary>
    ///     The funding payments in time order.
    /// </summary>
    public IReadOnlyList<FundingPayment> Payments { get; }

    /// <summary>
    ///     The events of <c>event.py</c>: instrument and close time.
    /// </summary>
    public IReadOnlyList<(string Instrument, DateTimeOffset CloseTime)> Events { get; }

    private ParityFixture(
        IReadOnlyList<ClosedCandle> candles,
        IReadOnlyList<FundingPayment> payments,
        IReadOnlyList<(string Instrument, DateTimeOffset CloseTime)> events)
    {
        Candles = candles;
        Payments = payments;
        Events = events;
    }

    /// <summary>
    ///     Reads the fixture from the output directory of the tests.
    /// </summary>
    public static ParityFixture Load()
    {
        var candles = Rows("bars.csv").Select(row => new ClosedCandle(row[0], Time(row[1]), Time(row[2]))).ToList();
        var payments = Rows("funding.csv")
            .Select(row => new FundingPayment(row[0], Time(row[1]), decimal.Parse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture)))
            .ToList();
        List<(string Instrument, DateTimeOffset CloseTime)> events = Rows("events.csv").Select(row => (row[0], Time(row[1]))).ToList();
        return new ParityFixture(candles, payments, events);
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Graphs.Fixture(file)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}