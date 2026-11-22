using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The rates of funding of the fake market-data service told live (change add-ideas-and-market, task 5.2).
/// </summary>
public sealed class FakeFundingRateFeed(FakeMarketData data) : FundingRateFeed.FundingRateFeedBase
{
    public override Task Subscribe(SubscribeFundingRateRequest request, IServerStreamWriter<FundingRateUpdate> responseStream, ServerCallContext context)
    {
        return data.Rates.Serve(request.Since?.ToDateTimeOffset(), forming: false, responseStream, context);
    }
}

/// <summary>
///     The points of open interest of the fake market-data service told live (change add-ideas-and-market, task 5.2).
/// </summary>
public sealed class FakeOpenInterestFeed(FakeMarketData data) : OpenInterestFeed.OpenInterestFeedBase
{
    public override Task Subscribe(SubscribeOpenInterestRequest request, IServerStreamWriter<OpenInterestUpdate> responseStream, ServerCallContext context)
    {
        return data.OpenInterest.Serve(request.Since?.ToDateTimeOffset(), forming: false, responseStream, context);
    }
}
