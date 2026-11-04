using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBotCandles;

public sealed class GetBotCandlesQueryHandler(IStrategyLibraryRepository library, CandleReadThrough readThrough, TimeProvider time)
    : IRequestHandler<GetBotCandlesQuery, GetBotCandlesResult?>
{
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
            errors["interval"] = ChartCandles.InvalidInterval;
        }

        (IReadOnlyList<Candle>? candles, bool unavailable) = await ChartCandles.ReadAsync(readThrough, time, bot.Instrument, interval, request.From, request.To, errors, cancellationToken);

        return new GetBotCandlesResult(interval, candles, unavailable, errors);
    }
}