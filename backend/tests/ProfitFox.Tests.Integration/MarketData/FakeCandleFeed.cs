using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The candle feed of the fake market-data service: coverage first, then candles of any interval built from its minutes; the minutes of 08:00 have no trades, and a 4h candle keeps the answer it had before: the one of 08:00 has no trades. A
///     window that ends after the start of the current day is refused; a dropped read breaks off after the first candle.
/// </summary>
public sealed class FakeCandleFeed(FakeMarketData data) : CandleFeed.CandleFeedBase
{
    /// <summary>
    ///     The live minutes (change add-ideas-and-market, task 5.2): every exchange's, from <c>since</c>, with the forming ones
    ///     when asked.
    /// </summary>
    public override Task Subscribe(SubscribeRequest request, IServerStreamWriter<CandleUpdate> responseStream, ServerCallContext context)
    {
        return data.Minutes.Serve(request.Since?.ToDateTimeOffset(), request.Forming, responseStream, context);
    }

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

        bool drop = data.TakeDrop();
        string instrument = request.InstrumentIds.Single();
        InstrumentCoverage coverage = FakeMarketData.CoverageOf(instrument, request.From, request.To);
        await responseStream.WriteAsync(new CandleHistoryItem { Coverage = coverage });
        TimeSpan step = Step(request.Interval);
        foreach (ProfitFox.Infrastructure.Clients.MarketData.Protos.TimeRange range in coverage.Ranges)
        {
            for (var open = range.From.ToDateTimeOffset(); open < range.Until.ToDateTimeOffset(); open += step)
            {
                CandleUpdate candle = request.Interval == CandleInterval.FourHours
                    ? Minute(instrument, open, open.Hour == 8 ? "0" : "12.5", "6")
                    : Built(instrument, open, step, range.Until.ToDateTimeOffset());
                if (data.Closes.TryGetValue(instrument, out string? close))
                {
                    candle.Close = close;
                }

                await responseStream.WriteAsync(new CandleHistoryItem { Candle = candle });
                if (drop)
                {
                    throw FakeMarketData.Drop(context);
                }
            }
        }
    }

    /// <summary>
    ///     The length of an interval of the service.
    /// </summary>
    public static TimeSpan Step(CandleInterval interval)
    {
        return interval switch
        {
            CandleInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            CandleInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            CandleInterval.OneHour => TimeSpan.FromHours(1),
            CandleInterval.FourHours => TimeSpan.FromHours(4),
            CandleInterval.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1)
        };
    }

    /// <summary>
    ///     The minute of the fake at <paramref name="open" />: the same prices every minute; the minutes of 08:00 have no
    ///     trades. A candle of 4h keeps the answer it had before the fake built intervals from minutes: the one of 08:00 has
    ///     no trades, the others have the volume of one minute.
    /// </summary>
    public static CandleUpdate Minute(string instrument, DateTimeOffset open, string? volume = null, string? buyVolume = null)
    {
        return new CandleUpdate
        {
            InstrumentId = instrument,
            Exchange = ProfitFox.Infrastructure.Clients.MarketData.Protos.Exchange.Bybit,
            OpenTime = Timestamp.FromDateTimeOffset(open),
            Open = "100.5",
            High = "101",
            Low = "100",
            Close = "100.25",
            Volume = volume ?? (open.Hour == 8 ? "0" : "12.5"),
            BuyVolume = buyVolume ?? (open.Hour == 8 ? "0" : "6"),
            Closed = true
        };
    }

    // A candle of an interval built from the minutes of its window inside the coverage: the prices of the minutes and the
    // sums of their volumes. A minute keeps the answer the fake always gave.
    private static CandleUpdate Built(string instrument, DateTimeOffset open, TimeSpan step, DateTimeOffset until)
    {
        if (step == TimeSpan.FromMinutes(1))
        {
            return Minute(instrument, open, buyVolume: "6");
        }

        decimal volume = 0;
        decimal buy = 0;
        for (DateTimeOffset minute = open; minute < open + step && minute < until; minute = minute.AddMinutes(1))
        {
            if (minute.Hour != 8)
            {
                volume += 12.5m;
                buy += 6m;
            }
        }

        string Text(decimal value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Minute(instrument, open, Text(volume), Text(buy));
    }
}
