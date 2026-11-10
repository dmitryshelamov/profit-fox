using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611100001)]
public sealed class CreateTradeFlowTablesMigration : ForwardOnlyMigration
{
    // Decisions 2, 7 and 8 of collect-trade-flow. The minutes of the trade flow are kept for good: daily chunks, compressed by pair once they
    // are 7 days old, still replaced by the rebuild of a day from the archive. A day of a pair read from the archive is a row of trade_flow_days.
    // One row holds the lease of the one collector that keeps the flow. The image before this one reads and writes none of the new tables.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE trade_flow_minutes (
                category text NOT NULL,
                symbol text NOT NULL,
                open_time timestamp with time zone NOT NULL,
                buy_volume numeric NOT NULL,
                sell_volume numeric NOT NULL,
                buy_turnover numeric NOT NULL,
                sell_turnover numeric NOT NULL,
                buy_trades integer NOT NULL,
                sell_trades integer NOT NULL,
                CONSTRAINT "PK_trade_flow_minutes" PRIMARY KEY (category, symbol, open_time)
            );

            SELECT create_hypertable('trade_flow_minutes', by_range('open_time', INTERVAL '1 day'));

            ALTER TABLE trade_flow_minutes SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'category, symbol',
                timescaledb.compress_orderby = 'open_time'
            );

            SELECT add_compression_policy('trade_flow_minutes', INTERVAL '7 days');

            CREATE TABLE trade_flow_days (
                category text NOT NULL,
                symbol text NOT NULL,
                day timestamp with time zone NOT NULL,
                status text NOT NULL,
                "at" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_trade_flow_days" PRIMARY KEY (category, symbol, day)
            );

            CREATE TABLE trade_flow_state (
                id integer NOT NULL,
                lease_owner uuid NULL,
                leased_until timestamp with time zone NULL,
                CONSTRAINT "PK_trade_flow_state" PRIMARY KEY (id),
                CONSTRAINT "CK_trade_flow_state_one_row" CHECK (id = 1)
            );

            INSERT INTO trade_flow_state (id) VALUES (1);
            """);
    }
}