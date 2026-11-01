using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Presentation.Contracts.Bots;

public sealed record BotSummaryResponse(Guid Id, string Name, Guid StrategyId, string Category, string Symbol, string Interval)
{
    public static BotSummaryResponse Of(Bot bot)
    {
        return new BotSummaryResponse(bot.Id, bot.Name, bot.StrategyId, bot.Instrument.Category.ToCode(), bot.Instrument.Symbol, bot.Interval.ToCode());
    }
}