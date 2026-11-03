using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBotCandles;

public sealed class GetBotCandlesQueryHandler(IStrategyLibraryRepository library, CandleReadThrough readThrough, TimeProvider time)
    : IRequestHandler<GetBotCandlesQuery, GetBotCandlesResult?>
{
    // The most candles of one page of the exchange.
    private const int MaxCandles = 1000;

    public async Task<GetBotCandlesResult?> Handle(GetBotCandlesQuery request, CancellationToken cancellationToken)
    {
        Bot? bot = await library.GetBotAsync(request.BotId, cancellationToken);

        if (bot is null)
        {
            return null;
        }

        var errors = new Dictionary<string, string>();
        Interval interval = bot.Interval;

        if (request.Interval is not null && !MarketDataCodes.TryParseInterval(request.Interval, out interval))
        {
            errors["interval"] = "Interval must be one of Bybit intervals: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M.";
        }

        if (!ApiTimestamps.TryParse(request.From, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        bool toIsTime = ApiTimestamps.TryParse(request.To, out DateTimeOffset to);

        if (!toIsTime)
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        // An unparsed `from` stays at its minimum and never fails the period check.
        if (toIsTime && from >= to)
        {
            errors["from"] = "from must be earlier than to.";
        }

        if (errors.Count > 0)
        {
            return new GetBotCandlesResult(interval, Candles: null, Unavailable: false, errors);
        }

        // A chart shows closed candles only: the current candle starts where the period ends at the latest.
        DateTimeOffset start = interval.CandleOpen(from);
        DateTimeOffset current = interval.CandleOpen(time.GetUtcNow() - CandleReadThrough.CloseMargin);
        DateTimeOffset end = to.ToUniversalTime() < current ? to.ToUniversalTime() : current;

        if (start >= end)
        {
            return new GetBotCandlesResult(interval, [], Unavailable: false, errors);
        }

        if (interval.CandleCount(start, end, MaxCandles) > MaxCandles)
        {
            errors["from"] = $"The period must not be longer than {MaxCandles} candles.";

            return new GetBotCandlesResult(interval, Candles: null, Unavailable: false, errors);
        }

        CandleReadThroughResult read = await readThrough.ReadAsync(bot.Instrument, interval, start, end, cancellationToken);

        return new GetBotCandlesResult(interval, read.Candles, read.Unavailable, errors);
    }
}