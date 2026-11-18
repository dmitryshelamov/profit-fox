using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProfitFox.Bench.MarketData.Protos;

namespace ProfitFox.Tests.Integration.Bench.MarketData;

/// <summary>
///     What the fake market-data service answers: a coverage of the whole window from <see cref="CoveredFrom" />, 4h or 1m
///     candles in it, a payment every 8 hours, or an error when <see cref="Failure" /> is set. It records the requests.
/// </summary>
public sealed class FakeMarketData
{
    public static readonly DateTimeOffset CoveredFrom = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public List<CandleHistoryRequest> CandleRequests { get; } = [];

    public List<FundingPaymentHistoryRequest> FundingRequests { get; } = [];

    public StatusCode? Failure { get; set; }

    public static InstrumentCoverage CoverageOf(string instrument, Timestamp from, Timestamp to)
    {
        DateTimeOffset start = from.ToDateTimeOffset() > CoveredFrom ? from.ToDateTimeOffset() : CoveredFrom;
        var coverage = new InstrumentCoverage { InstrumentId = instrument };
        if (start < to.ToDateTimeOffset())
        {
            coverage.Ranges.Add(new TimeRange { From = Timestamp.FromDateTimeOffset(start), Until = to });
        }

        return coverage;
    }
}
