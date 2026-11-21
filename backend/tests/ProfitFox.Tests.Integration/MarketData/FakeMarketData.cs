using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProfitFox.Infrastructure.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     What the fake market-data service answers: a coverage of the whole window from <see cref="CoveredFrom" />, 4h or 1m
///     candles in it, a payment every 8 hours, an error when <see cref="Failure" /> is set, or a hold when <see cref="BeforeCandles" /> is set. It records the requests.
/// </summary>
public sealed class FakeMarketData
{
    public static readonly DateTimeOffset CoveredFrom = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public List<CandleHistoryRequest> CandleRequests { get; } = [];

    public List<FundingPaymentHistoryRequest> FundingRequests { get; } = [];

    public StatusCode? Failure { get; set; }

    /// <summary>
    ///     Awaited by every candle request after it is recorded and before it is answered, with the token of the call: a test
    ///     holds a run on a pair with it, and the hold ends when the run drops the call.
    /// </summary>
    public Func<CandleHistoryRequest, CancellationToken, Task>? BeforeCandles { get; set; }

    /// <summary>
    ///     Where the history ends, if anywhere: a shorter history keeps the tests fast.
    /// </summary>
    public static DateTimeOffset? CoveredUntil { get; set; }

    /// <summary>
    ///     The rate of the payment at <paramref name="time" />: 0.0006 for every third payment, 0.0001 otherwise, so the
    ///     rate crosses 0.05 % once a day.
    /// </summary>
    public static string RateAt(DateTimeOffset time)
    {
        long payment = (long)((time - CoveredFrom).TotalHours / 8);
        return payment % 3 == 1 ? "0.0006" : "0.0001";
    }

    public static InstrumentCoverage CoverageOf(string instrument, Timestamp from, Timestamp to)
    {
        DateTimeOffset start = from.ToDateTimeOffset() > CoveredFrom ? from.ToDateTimeOffset() : CoveredFrom;
        DateTimeOffset end = CoveredUntil is { } until && until < to.ToDateTimeOffset() ? until : to.ToDateTimeOffset();
        var coverage = new InstrumentCoverage { InstrumentId = instrument };
        if (start < end)
        {
            coverage.Ranges.Add(new TimeRange { From = Timestamp.FromDateTimeOffset(start), Until = Timestamp.FromDateTimeOffset(end) });
        }

        return coverage;
    }
}
