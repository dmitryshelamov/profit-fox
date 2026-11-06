using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>The daily stop as the real trades stand: over the trades that closed.</summary>
public static class DailyLossReader
{
    /// <summary>The state of the daily stop: the sum of the closed real trades of the day of <paramref name="now" /> against <paramref name="limit" />.</summary>
    public static DailyLossState Of(IEnumerable<IdeaTrade> trades, decimal? limit, DateTimeOffset now)
    {
        return DailyLossStop.Of(trades.Where(trade => trade.Result is not null).Select(trade => (trade.StatusAt, trade.Result!.Value)), now, limit);
    }
}