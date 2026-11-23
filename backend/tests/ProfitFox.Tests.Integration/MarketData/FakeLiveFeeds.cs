using Google.Protobuf.WellKnownTypes;

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
///     The points of open interest of the fake market-data service: told live (change add-ideas-and-market, task 5.2) and,
///     as the real service, the stored ones of past days by <c>History</c> (task 18.2) — the coverage of every instrument
///     first, then a point of 24 000 coins every 5 minutes; a window that ends after the start of the current UTC day is
///     refused.
/// </summary>
public sealed class FakeOpenInterestFeed(FakeMarketData data) : OpenInterestFeed.OpenInterestFeedBase
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    public override Task Subscribe(SubscribeOpenInterestRequest request, IServerStreamWriter<OpenInterestUpdate> responseStream, ServerCallContext context)
    {
        return data.OpenInterest.Serve(request.Since?.ToDateTimeOffset(), forming: false, responseStream, context);
    }

    public override async Task History(OpenInterestHistoryRequest request, IServerStreamWriter<OpenInterestHistoryItem> responseStream, ServerCallContext context)
    {
        lock (data.InterestRequests)
        {
            data.InterestRequests.Add(request);
        }

        if (data.BeforeInterest is { } before)
        {
            await before(request, context.CancellationToken);
        }

        if (data.Failure is { } failure)
        {
            throw new RpcException(new Status(failure, "the fake service is stopping"));
        }

        if (request.To.ToDateTimeOffset() <= request.From.ToDateTimeOffset())
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "the window must end after it starts"));
        }

        data.CheckPastDays(request.To);
        List<InstrumentCoverage> coverages = [.. request.InstrumentIds.Order(StringComparer.Ordinal).Select(id => FakeMarketData.CoverageOf(id, request.From, request.To))];
        foreach (InstrumentCoverage coverage in coverages)
        {
            await responseStream.WriteAsync(new OpenInterestHistoryItem { Coverage = coverage });
        }

        IEnumerable<(DateTimeOffset Time, string Id)> points = coverages
            .SelectMany(coverage => coverage.Ranges.SelectMany(range => Times(range.From.ToDateTimeOffset(), range.Until.ToDateTimeOffset()).Select(time => (time, coverage.InstrumentId))))
            .OrderBy(point => point.Item1)
            .ThenBy(point => point.Item2, StringComparer.Ordinal);
        foreach ((DateTimeOffset time, string id) in points)
        {
            await responseStream.WriteAsync(new OpenInterestHistoryItem
            {
                Point = new OpenInterestUpdate { InstrumentId = id, Time = Timestamp.FromDateTimeOffset(time), OpenInterest = "24000" }
            });
        }
    }

    private static IEnumerable<DateTimeOffset> Times(DateTimeOffset from, DateTimeOffset until)
    {
        long ticks = from.UtcTicks % Step.Ticks;
        for (DateTimeOffset time = ticks == 0 ? from : from.AddTicks(Step.Ticks - ticks); time < until; time += Step)
        {
            yield return time;
        }
    }
}
