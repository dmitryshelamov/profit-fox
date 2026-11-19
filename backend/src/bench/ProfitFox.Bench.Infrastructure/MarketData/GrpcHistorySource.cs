using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Domain.MarketData;
using ProfitFox.Bench.Infrastructure.MarketData.Protos;
using TimeRange = ProfitFox.Bench.Domain.MarketData.TimeRange;

namespace ProfitFox.Bench.Infrastructure.MarketData;

/// <summary>
///     History from the <c>History</c> streams of the market-data service (spec market-history), one instrument a call.
/// </summary>
public sealed class GrpcHistorySource(GrpcChannel channel) : IHistorySource
{
    private readonly CandleFeed.CandleFeedClient _candles = new CandleFeed.CandleFeedClient(channel);
    private readonly FundingPaymentFeed.FundingPaymentFeedClient _funding = new FundingPaymentFeed.FundingPaymentFeedClient(channel);

    public async Task<HistoryRead<HistoryCandle>> ReadCandles(
        string instrumentId,
        TimeSpan interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellation)
    {
        var request = new CandleHistoryRequest
        {
            InstrumentIds = { instrumentId },
            From = Timestamp.FromDateTimeOffset(from),
            To = Timestamp.FromDateTimeOffset(to),
            Interval = Intervals.ToProto(interval)
        };
        List<TimeRange> coverage = [];
        List<HistoryCandle> candles = [];
        await Read(async () =>
        {
            using AsyncServerStreamingCall<CandleHistoryItem> call = _candles.History(request, cancellationToken: cancellation);
            await foreach (CandleHistoryItem item in call.ResponseStream.ReadAllAsync(cancellation))
            {
                if (item.ItemCase == CandleHistoryItem.ItemOneofCase.Coverage)
                {
                    coverage.AddRange(Ranges(item.Coverage));
                }
                else if (item.ItemCase == CandleHistoryItem.ItemOneofCase.Candle)
                {
                    CandleUpdate candle = item.Candle;
                    candles.Add(new HistoryCandle(
                        candle.OpenTime.ToDateTimeOffset(),
                        candle.Open,
                        candle.High,
                        candle.Low,
                        candle.Close,
                        candle.Volume,
                        candle.HasTurnover ? candle.Turnover : null,
                        candle.HasBuyVolume ? candle.BuyVolume : null));
                }
            }
        });

        return new HistoryRead<HistoryCandle>(coverage, candles);
    }

    public async Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        var request = new FundingPaymentHistoryRequest
        {
            InstrumentIds = { instrumentId },
            From = Timestamp.FromDateTimeOffset(from),
            To = Timestamp.FromDateTimeOffset(to)
        };
        List<TimeRange> coverage = [];
        List<HistoryPayment> payments = [];
        await Read(async () =>
        {
            using AsyncServerStreamingCall<FundingPaymentHistoryItem> call = _funding.History(request, cancellationToken: cancellation);
            await foreach (FundingPaymentHistoryItem item in call.ResponseStream.ReadAllAsync(cancellation))
            {
                if (item.ItemCase == FundingPaymentHistoryItem.ItemOneofCase.Coverage)
                {
                    coverage.AddRange(Ranges(item.Coverage));
                }
                else if (item.ItemCase == FundingPaymentHistoryItem.ItemOneofCase.Payment)
                {
                    payments.Add(new HistoryPayment(item.Payment.Time.ToDateTimeOffset(), item.Payment.FundingRate));
                }
            }
        });

        return new HistoryRead<HistoryPayment>(coverage, payments);
    }

    private static IEnumerable<TimeRange> Ranges(InstrumentCoverage coverage)
    {
        return coverage.Ranges.Select(range => new TimeRange(range.From.ToDateTimeOffset(), range.Until.ToDateTimeOffset()));
    }

    private static async Task Read(Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal)
        {
            throw new HistoryUnavailableException($"The market-data service did not give the history: {exception.Status.Detail}", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new HistoryUnavailableException("The market-data service did not answer.", exception);
        }
    }
}
