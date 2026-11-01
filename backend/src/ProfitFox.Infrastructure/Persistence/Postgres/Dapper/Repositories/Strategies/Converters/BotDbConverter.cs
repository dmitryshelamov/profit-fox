using System.Text.Json;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

internal static class BotDbConverter
{
    internal static BotDb FromDomain(Bot bot)
    {
        return new BotDb
        {
            Id = bot.Id,
            Name = bot.Name,
            StrategyId = bot.StrategyId,
            ParameterValues = StrategyJson.WriteValues(bot.ParameterValues).ToJsonString(),
            Category = bot.Instrument.Category.ToCode(),
            Symbol = bot.Instrument.Symbol,
            Interval = bot.Interval.ToCode(),
            Balance = bot.Balance,
            CreatedAt = bot.CreatedAt,
            UpdatedAt = bot.UpdatedAt
        };
    }

    internal static Bot ToDomain(BotDb bot)
    {
        using var values = JsonDocument.Parse(bot.ParameterValues);
        MarketDataCodes.TryParseCategory(bot.Category, out MarketCategory category);
        MarketDataCodes.TryParseInterval(bot.Interval, out Interval interval);

        return new Bot(
            bot.Id,
            bot.Name,
            bot.StrategyId,
            StrategyJson.ParseValues(values.RootElement),
            new Instrument(category, bot.Symbol),
            interval,
            bot.Balance,
            bot.CreatedAt,
            bot.UpdatedAt);
    }
}