using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611050002)]
public sealed class CreateLiveTradingTablesMigration : ForwardOnlyMigration
{
    // A bot in live trading, its positions, orders and fills; all of it goes with the bot. The lease epoch grows with every
    // claim, so a Worker that lost a bot and took it again is told from its stalled self. An exec id of the exchange is a
    // fill once. Only known codes: a status that is not one must never be read as one.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE live_bots (
                bot_id uuid NOT NULL,
                status text NOT NULL,
                stop_reason text NULL,
                strategy_version integer NULL,
                parameter_values jsonb NOT NULL,
                start_pending boolean NOT NULL,
                close_requested boolean NOT NULL,
                last_order_number integer NOT NULL,
                last_bar_at timestamp with time zone NULL,
                lease_owner uuid NULL,
                leased_until timestamp with time zone NULL,
                lease_epoch bigint NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_live_bots" PRIMARY KEY (bot_id),
                CONSTRAINT "FK_live_bots_bots" FOREIGN KEY (bot_id) REFERENCES bots (id) ON DELETE CASCADE,
                CONSTRAINT "CK_live_bots_status" CHECK (status IN ('stopped', 'running', 'paused', 'stopping', 'failed'))
            );

            CREATE TABLE live_positions (
                bot_id uuid NOT NULL,
                number integer NOT NULL,
                open boolean NOT NULL,
                closing boolean NOT NULL,
                quantity numeric NOT NULL,
                invested numeric NOT NULL,
                collateral numeric NOT NULL,
                realized_pnl numeric NOT NULL,
                fees numeric NOT NULL,
                last_entry_price numeric NULL,
                last_entry_at timestamp with time zone NULL,
                skipped_orders bigint NOT NULL,
                closed_at timestamp with time zone NULL,
                CONSTRAINT "PK_live_positions" PRIMARY KEY (bot_id, number),
                CONSTRAINT "FK_live_positions_bots" FOREIGN KEY (bot_id) REFERENCES bots (id) ON DELETE CASCADE
            );

            CREATE TABLE live_orders (
                bot_id uuid NOT NULL,
                number integer NOT NULL,
                position_number integer NOT NULL,
                node_path text NULL,
                side text NOT NULL,
                type text NOT NULL,
                price numeric NULL,
                quantity numeric NULL,
                amount numeric NULL,
                reserve numeric NOT NULL,
                status text NOT NULL,
                filled_quantity numeric NOT NULL,
                filled_value numeric NOT NULL,
                fee numeric NOT NULL,
                exchange_order_id text NULL,
                reject_reason text NULL,
                attempts integer NOT NULL,
                next_attempt_at timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_live_orders" PRIMARY KEY (bot_id, number),
                CONSTRAINT "FK_live_orders_live_positions" FOREIGN KEY (bot_id, position_number) REFERENCES live_positions (bot_id, number) ON DELETE CASCADE,
                CONSTRAINT "CK_live_orders_side" CHECK (side IN ('buy', 'sell')),
                CONSTRAINT "CK_live_orders_type" CHECK (type IN ('market', 'limit')),
                CONSTRAINT "CK_live_orders_status" CHECK (status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent', 'filled', 'cancelled', 'rejected'))
            );

            CREATE INDEX "IX_live_orders_unfinished" ON live_orders (bot_id, number)
                WHERE status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent');

            CREATE TABLE live_fills (
                exec_id text NOT NULL,
                bot_id uuid NOT NULL,
                order_number integer NOT NULL,
                quantity numeric NOT NULL,
                price numeric NOT NULL,
                fee numeric NOT NULL,
                fee_asset text NOT NULL,
                executed_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_live_fills" PRIMARY KEY (exec_id),
                CONSTRAINT "FK_live_fills_live_orders" FOREIGN KEY (bot_id, order_number) REFERENCES live_orders (bot_id, number) ON DELETE CASCADE,
                CONSTRAINT "CK_live_fills_fee_asset" CHECK (fee_asset IN ('quote', 'base'))
            );
            """);
    }
}