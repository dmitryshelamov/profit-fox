using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The candle feed of the fake market-data service: coverage first, then 4h or 1m candles; the 08:00 ones have no trades. A
///     window that ends after the start of the current day is refused.
/// </summary>
public sealed class FakeCandleFeed(FakeMarketData data) : CandleFeed.CandleFeedBase
{
    public override async Task History(CandleHistoryRequest request, IServerStreamWriter<CandleHistoryItem> responseStream, ServerCallContext context)
    {
        lock (data.CandleRequests)
        {
            data.CandleRequests.Add(request);
        }

        if (data.BeforeCandles is { } before)
        {
            await before(request, context.CancellationToken);
        }

        if (data.Failure is { } failure)
        {
            throw new RpcException(new Status(failure, "the fake service is stopping"));
        }

        data.CheckPastDays(request.To);

        string instrument = request.InstrumentIds.Single();
        InstrumentCoverage coverage = FakeMarketData.CoverageOf(instrument, request.From, request.To);
        await responseStream.WriteAsync(new CandleHistoryItem { Coverage = coverage });
        TimeSpan step = request.Interval == CandleInterval.FourHours ? TimeSpan.FromHours(4) : TimeSpan.FromMinutes(1);
        foreach (ProfitFox.Infrastructure.Clients.MarketData.Protos.TimeRange range in coverage.Ranges)
        {
            for (var open = range.From.ToDateTimeOffset(); open < range.Until.ToDateTimeOffset(); open += step)
            {
                var candle = new CandleUpdate
                {
                    InstrumentId = instrument,
                    Exchange = Exchange.Bybit,
                    OpenTime = Timestamp.FromDateTimeOffset(open),
                    Open = "100.5",
                    High = "101",
                    Low = "100",
                    Close = "100.25",
                    Volume = open.Hour == 8 ? "0" : "12.5",
                    BuyVolume = "6",
                    Closed = true
                };
                await responseStream.WriteAsync(new CandleHistoryItem { Candle = candle });
            }
        }
    }
}
