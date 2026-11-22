using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.Infrastructure.Clients.MarketData.Protos;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     What the fake market-data service answers: a coverage of the whole window from <see cref="CoveredFrom" />, 4h or 1m
///     candles in it, a payment every 8 hours, an error when <see cref="Failure" /> is set, a broken stream while <see cref="Drops" /> lasts, or a hold when <see cref="BeforeCandles" /> is set. It records the requests.
///     As the real service, it holds past days only and refuses a window that ends after the start of the current UTC day of <see cref="Time" />.
/// </summary>
public sealed class FakeMarketData
{
    public static readonly DateTimeOffset CoveredFrom = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    public List<CandleHistoryRequest> CandleRequests { get; } = [];

    public List<FundingPaymentHistoryRequest> FundingRequests { get; } = [];

    public StatusCode? Failure { get; set; }

    private int _drops;

    /// <summary>
    ///     How many of the next reads, candles or funding, the service breaks off after the coverage and the first item, as a
    ///     dropped connection does (task 12.12): the client is left with a broken stream and part of the history.
    /// </summary>
    public int Drops
    {
        get => Volatile.Read(ref _drops);
        set => Volatile.Write(ref _drops, value);
    }

    /// <summary>
    ///     The clock of the test: the current UTC day by it is not held yet. The API of a test sets it to its own clock.
    /// </summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

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

    /// <summary>
    ///     Refuses, as the real service does, a window that ends after the start of the current UTC day: that day is not
    ///     held yet.
    /// </summary>
    public void CheckPastDays(Timestamp to)
    {
        DateTimeOffset now = Time.GetUtcNow();
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        if (to.ToDateTimeOffset() > today)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"the window must end at {today:yyyy-MM-ddTHH:mm} or before"));
        }
    }

    /// <summary>
    ///     Whether this read is to be broken off: one of <see cref="Drops" /> is used up by it.
    /// </summary>
    public bool TakeDrop()
    {
        int drops;
        do
        {
            drops = Volatile.Read(ref _drops);
            if (drops <= 0)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _drops, drops - 1, drops) != drops);

        return true;
    }

    /// <summary>
    ///     Breaks off the call as a dropped connection does: the client reads a broken stream.
    /// </summary>
    public static Exception Drop(ServerCallContext context)
    {
        context.GetHttpContext().Abort();
        return new IOException("the fake service dropped the read");
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
