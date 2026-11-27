using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The funding feed of the fake market-data service: coverage first, then a payment every 8 hours with the rates of <see cref="FakeMarketData.RateAt" />.
///     A window that ends after the start of the current day is refused; a dropped read breaks off after the first payment.
/// </summary>
public sealed class FakeFundingFeed(FakeMarketData data) : FundingPaymentFeed.FundingPaymentFeedBase
{
    /// <summary>
    ///     The live payments (change add-ideas-and-market, task 5.2).
    /// </summary>
    public override Task Subscribe(SubscribeFundingPaymentRequest request, IServerStreamWriter<FundingPaymentUpdate> responseStream, ServerCallContext context)
    {
        return data.Payments.Serve(request.Since?.ToDateTimeOffset(), forming: false, responseStream, context);
    }

    public override async Task History(
        FundingPaymentHistoryRequest request,
        IServerStreamWriter<FundingPaymentHistoryItem> responseStream,
        ServerCallContext context)
    {
        data.FundingRequests.Add(request);
        data.CheckPastDays(request.To);
        bool drop = data.TakeDrop();
        string instrument = request.InstrumentIds.Single();
        InstrumentCoverage coverage = FakeMarketData.CoverageOf(instrument, request.From, request.To);
        await responseStream.WriteAsync(new FundingPaymentHistoryItem { Coverage = coverage });
        foreach (ProfitFox.Infrastructure.Clients.MarketData.Protos.TimeRange range in coverage.Ranges)
        {
            for (var time = range.From.ToDateTimeOffset(); time < range.Until.ToDateTimeOffset(); time += TimeSpan.FromHours(8))
            {
                var payment = new FundingPaymentUpdate { InstrumentId = instrument, Time = Timestamp.FromDateTimeOffset(time), FundingRate = FakeMarketData.RateAt(time) };
                await responseStream.WriteAsync(new FundingPaymentHistoryItem { Payment = payment });
                if (drop)
                {
                    throw FakeMarketData.Drop(context);
                }
            }
        }
    }
}
