using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The settings of the risk module in the one row of <c>trade_settings</c> (change take-idea-trades, design.md, decision
///     12). The times and the attempts not saved read as their defaults.
/// </summary>
internal sealed class TradeSettingsRepository(NpgsqlDataSource database) : ITradeSettingsRepository
{
    public async Task<TradeSettings> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT default_account_id AS "DefaultAccountId", default_amount AS "DefaultAmount", leverage AS "Leverage", max_amount AS "MaxAmount",
                   max_open_trades AS "MaxOpenTrades", max_total_amount AS "MaxTotalAmount", max_loss_percent AS "MaxLossPercent", max_day_loss AS "MaxDayLoss",
                   expiry_margin_minutes AS "ExpiryMarginMinutes", entry_wait_seconds AS "EntryWaitSeconds", stop_attempts AS "StopAttempts", saved_at AS "SavedAt"
            FROM trade_settings WHERE id = 1
            """,
            cancellationToken: cancellationToken));
        return row is null
            ? TradeSettings.Empty
            : new TradeSettings
            {
                DefaultAccountId = row.DefaultAccountId,
                DefaultAmount = row.DefaultAmount,
                Leverage = row.Leverage,
                MaxAmount = row.MaxAmount,
                MaxOpenTrades = row.MaxOpenTrades,
                MaxTotalAmount = row.MaxTotalAmount,
                MaxLossPercent = row.MaxLossPercent,
                MaxDayLoss = row.MaxDayLoss,
                ExpiryMargin = row.ExpiryMarginMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : TradeSettings.DefaultExpiryMargin,
                EntryWait = row.EntryWaitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : TradeSettings.DefaultEntryWait,
                StopAttempts = row.StopAttempts ?? TradeSettings.DefaultStopAttempts,
                SavedAt = row.SavedAt
            };
    }

    public async Task SaveAsync(TradeSettings settings, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO trade_settings (id, default_account_id, default_amount, leverage, max_amount, max_open_trades, max_total_amount, max_loss_percent, max_day_loss,
                                        expiry_margin_minutes, entry_wait_seconds, stop_attempts, saved_at)
            VALUES (1, @DefaultAccountId, @DefaultAmount, @Leverage, @MaxAmount, @MaxOpenTrades, @MaxTotalAmount, @MaxLossPercent, @MaxDayLoss,
                    @ExpiryMarginMinutes, @EntryWaitSeconds, @StopAttempts, @SavedAt)
            ON CONFLICT (id) DO UPDATE SET
                default_account_id = EXCLUDED.default_account_id, default_amount = EXCLUDED.default_amount, leverage = EXCLUDED.leverage,
                max_amount = EXCLUDED.max_amount, max_open_trades = EXCLUDED.max_open_trades, max_total_amount = EXCLUDED.max_total_amount,
                max_loss_percent = EXCLUDED.max_loss_percent, max_day_loss = EXCLUDED.max_day_loss, expiry_margin_minutes = EXCLUDED.expiry_margin_minutes,
                entry_wait_seconds = EXCLUDED.entry_wait_seconds, stop_attempts = EXCLUDED.stop_attempts, saved_at = EXCLUDED.saved_at
            """,
            new
            {
                settings.DefaultAccountId,
                settings.DefaultAmount,
                settings.Leverage,
                settings.MaxAmount,
                settings.MaxOpenTrades,
                settings.MaxTotalAmount,
                settings.MaxLossPercent,
                settings.MaxDayLoss,
                ExpiryMarginMinutes = (int)settings.ExpiryMargin.TotalMinutes,
                EntryWaitSeconds = (int)settings.EntryWait.TotalSeconds,
                settings.StopAttempts,
                SavedAt = settings.SavedAt ?? DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public Guid? DefaultAccountId { get; init; }

        public decimal? DefaultAmount { get; init; }

        public int? Leverage { get; init; }

        public decimal? MaxAmount { get; init; }

        public int? MaxOpenTrades { get; init; }

        public decimal? MaxTotalAmount { get; init; }

        public decimal? MaxLossPercent { get; init; }

        public decimal? MaxDayLoss { get; init; }

        public int? ExpiryMarginMinutes { get; init; }

        public int? EntryWaitSeconds { get; init; }

        public int? StopAttempts { get; init; }

        public DateTimeOffset SavedAt { get; init; }
    }
}
