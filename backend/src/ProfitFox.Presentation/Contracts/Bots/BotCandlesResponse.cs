using ProfitFox.Application.Bots.Queries.GetBotCandles;
using ProfitFox.Application.MarketData;
using ProfitFox.Presentation.Contracts.MarketData;

namespace ProfitFox.Presentation.Contracts.Bots;

/// <summary>Closed candles of a bot's instrument, oldest first; <see cref="Unavailable" /> says Bybit could not fill the period.</summary>
public sealed record BotCandlesResponse(string Interval, IReadOnlyList<CandleResponse> Candles, bool Unavailable)
{
    public static BotCandlesResponse Of(GetBotCandlesResult result)
    {
        return new BotCandlesResponse(result.Interval.ToCode(), [.. result.Candles!.Select(CandleResponse.Of)], result.Unavailable);
    }
}