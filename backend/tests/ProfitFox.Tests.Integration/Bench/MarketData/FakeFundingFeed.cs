using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProfitFox.Bench.Infrastructure.MarketData.Protos;

namespace ProfitFox.Tests.Integration.Bench.MarketData;

/// <summary>
///     The funding feed of the fake market-data service: coverage first, then a payment every 8 hours with the rates of <see cref="FakeMarketData.RateAt" />.
/// </summary>
public sealed class FakeFundingFeed(FakeMarketData data) : FundingPaymentFeed.FundingPaymentFeedBase
{
    public override async Task History(
        FundingPaymentHistoryRequest request,
        IServerStreamWriter<FundingPaymentHistoryItem> responseStream,
        ServerCallContext context)
    {
        data.FundingRequests.Add(request);
        string instrument = request.InstrumentIds.Single();
        InstrumentCoverage coverage = FakeMarketData.CoverageOf(instrument, request.From, request.To);
        await responseStream.WriteAsync(new FundingPaymentHistoryItem { Coverage = coverage });
        foreach (ProfitFox.Bench.Infrastructure.MarketData.Protos.TimeRange range in coverage.Ranges)
        {
            for (DateTimeOffset time = range.From.ToDateTimeOffset(); time < range.Until.ToDateTimeOffset(); time += TimeSpan.FromHours(8))
            {
                var payment = new FundingPaymentUpdate { InstrumentId = instrument, Time = Timestamp.FromDateTimeOffset(time), FundingRate = FakeMarketData.RateAt(time) };
                await responseStream.WriteAsync(new FundingPaymentHistoryItem { Payment = payment });
            }
        }
    }
}
