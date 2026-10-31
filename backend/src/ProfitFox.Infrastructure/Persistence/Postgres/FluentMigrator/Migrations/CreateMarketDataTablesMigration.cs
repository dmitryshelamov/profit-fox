using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202610310001)]
public sealed class CreateMarketDataTablesMigration : ForwardOnlyMigration
{
    // Unconstrained numeric keeps every value exactly as Bybit sent it.
    public override void Up() => Execute.Sql(
        """
        CREATE EXTENSION IF NOT EXISTS timescaledb;

        CREATE TABLE candle_import_jobs (
            id uuid NOT NULL,
            category text NOT NULL,
            symbol text NOT NULL,
            "interval" text NOT NULL,
            "from" timestamp with time zone NOT NULL,
            "to" timestamp with time zone NOT NULL,
            status text NOT NULL,
            imported_candles bigint NOT NULL,
            error text NULL,
            created_at timestamp with time zone NOT NULL,
            started_at timestamp with time zone NULL,
            finished_at timestamp with time zone NULL,
            CONSTRAINT "PK_candle_import_jobs" PRIMARY KEY (id)
        );

        CREATE TABLE candles (
            category text NOT NULL,
            symbol text NOT NULL,
            "interval" text NOT NULL,
            open_time timestamp with time zone NOT NULL,
            open numeric NOT NULL,
            high numeric NOT NULL,
            low numeric NOT NULL,
            close numeric NOT NULL,
            volume numeric NOT NULL,
            turnover numeric NOT NULL,
            CONSTRAINT "PK_candles" PRIMARY KEY (category, symbol, "interval", open_time)
        );

        SELECT create_hypertable('candles', by_range('open_time'));
        """);
}
