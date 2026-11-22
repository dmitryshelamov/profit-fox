using System.Globalization;

using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Positions;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies;

/// <summary>
///     Change add-position-node, task 11.6: the cycles of a strategy with orders in a preview stop being counted when the
///     request is cancelled. Hourly candles from <see cref="Paths.Start" />, the ladder of <c>Fixtures/ladder.json</c>.
/// </summary>
public sealed class PairCyclesTests
{
    private const int Hours = 6;

    [Test]
    public void The_cycles_are_counted_to_the_end_while_the_request_stands()
    {
        PositionResult result = Cycles(cancelAtHour: null, CancellationToken.None);

        Assert.That(result.Cycles, Is.Not.Empty);
    }

    [Test]
    public void A_cancelled_request_stops_the_calculation()
    {
        using var cancellation = new CancellationTokenSource();

        Assert.That(() => Cycles(cancelAtHour: 3, cancellation.Token, cancellation), Throws.InstanceOf<OperationCanceledException>());
    }

    // The ladder on six hours at one price; the request is cancelled when the candle of the given hour is read.
    private static PositionResult Cycles(int? cancelAtHour, CancellationToken token, CancellationTokenSource? cancellation = null)
    {
        MinutePath path = new Paths().Flat(100, Hours * 60).Build(withFunding: false);
        DateTimeOffset end = Paths.Start + TimeSpan.FromHours(Hours);
        return PairCycles.Of(PositionRuns.Strategy(Graphs.Ladder), TimeSpan.FromHours(1), Candles(), [], path, Paths.Start, end, PositionRuns.Free, token);

        IEnumerable<HistoryCandle> Candles()
        {
            for (int hour = 0; hour < Hours; hour++)
            {
                if (hour == cancelAtHour)
                {
                    cancellation!.Cancel();
                }

                string price = 100.ToString(CultureInfo.InvariantCulture);
                yield return new HistoryCandle(Paths.Start + TimeSpan.FromHours(hour - 1), price, price, price, price, "1", Turnover: null, BuyVolume: null);
            }
        }
    }
}
