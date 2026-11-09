using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;

internal sealed class IdeaTradingSettingsRepository(NpgsqlDataSource dataSource) : IIdeaTradingSettingsRepository
{
    public async Task<IdeaTradingSettings> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaTradingSettingsDb? settings = await connection.QuerySingleOrDefaultAsync<IdeaTradingSettingsDb>(
            new CommandDefinition(
                """
                SELECT account_id AS "AccountId", amount, max_amount AS "MaxAmount", max_open_trades AS "MaxOpenTrades",
                    max_total_risk AS "MaxTotalRisk", min_risk_reward AS "MinRiskReward", daily_loss_limit AS "DailyLossLimit",
                    max_same_direction AS "MaxSameDirection", autos_budget AS "AutosBudget", leverage
                FROM idea_trading_settings WHERE id = 1
                """,
                cancellationToken: cancellationToken));

        return settings is null ? IdeaTradingSettings.Default : IdeaTradeDbConverter.ToDomain(settings);
    }

    public async Task SaveAsync(IdeaTradingSettings settings, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_trading_settings (id, account_id, amount, max_amount, max_open_trades, max_total_risk, min_risk_reward, daily_loss_limit, max_same_direction, autos_budget, leverage)
                VALUES (1, @AccountId, @Amount, @MaxAmount, @MaxOpenTrades, @MaxTotalRisk, @MinRiskReward, @DailyLossLimit, @MaxSameDirection, @AutosBudget, @Leverage)
                ON CONFLICT (id) DO UPDATE SET account_id = EXCLUDED.account_id, amount = EXCLUDED.amount, max_amount = EXCLUDED.max_amount,
                    max_open_trades = EXCLUDED.max_open_trades, max_total_risk = EXCLUDED.max_total_risk, min_risk_reward = EXCLUDED.min_risk_reward,
                    daily_loss_limit = EXCLUDED.daily_loss_limit, max_same_direction = EXCLUDED.max_same_direction, autos_budget = EXCLUDED.autos_budget,
                    leverage = EXCLUDED.leverage
                """,
                settings,
                cancellationToken: cancellationToken));
    }
}