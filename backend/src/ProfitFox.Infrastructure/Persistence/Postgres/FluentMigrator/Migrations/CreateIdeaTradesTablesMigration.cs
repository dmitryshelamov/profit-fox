using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070005)]
public sealed class CreateIdeaTradesTablesMigration : ForwardOnlyMigration
{
    // A trade by an idea, its orders, the stop and target orders the exchange made for it, and its fills; all of it goes with the trade.
    // The idea is not a foreign key: the replay of ideas cleans up the ideas of old versions, and a trade outlives its idea. The lease
    // epoch grows with every claim, as that of a live bot. The row of the settings is the only one and is written on the first save.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE idea_trades (
                id uuid NOT NULL,
                idea_id uuid NOT NULL,
                account_id uuid NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                setup text NOT NULL,
                interval text NOT NULL,
                type text NOT NULL,
                side text NOT NULL,
                entry_price numeric NOT NULL,
                stop numeric NOT NULL,
                target numeric NOT NULL,
                quantity numeric NOT NULL,
                amount numeric NOT NULL,
                risk numeric NOT NULL,
                entry_deadline timestamp with time zone NULL,
                status text NOT NULL,
                status_at timestamp with time zone NOT NULL,
                reason text NULL,
                cancel_requested boolean NOT NULL,
                close_requested boolean NOT NULL,
                entry_quantity numeric NOT NULL,
                entry_value numeric NOT NULL,
                entry_fee numeric NOT NULL,
                take_profit_quantity numeric NOT NULL,
                take_profit_value numeric NOT NULL,
                take_profit_fee numeric NOT NULL,
                stop_loss_quantity numeric NOT NULL,
                stop_loss_value numeric NOT NULL,
                stop_loss_fee numeric NOT NULL,
                manual_quantity numeric NOT NULL,
                manual_value numeric NOT NULL,
                manual_fee numeric NOT NULL,
                last_order_number integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                lease_owner uuid NULL,
                leased_until timestamp with time zone NULL,
                lease_epoch bigint NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_trades" PRIMARY KEY (id),
                CONSTRAINT "FK_idea_trades_exchange_accounts" FOREIGN KEY (account_id) REFERENCES exchange_accounts (id),
                CONSTRAINT "CK_idea_trades_status" CHECK (status IN ('awaiting_entry', 'open', 'closed_at_target', 'closed_at_stop', 'closed_manually', 'cancelled', 'failed')),
                CONSTRAINT "CK_idea_trades_side" CHECK (side IN ('long', 'short'))
            );

            CREATE INDEX "IX_idea_trades_created_at" ON idea_trades (created_at DESC);
            CREATE INDEX "IX_idea_trades_kept" ON idea_trades (id) WHERE status IN ('awaiting_entry', 'open');

            CREATE TABLE idea_trade_orders (
                trade_id uuid NOT NULL,
                number integer NOT NULL,
                role text NOT NULL,
                side text NOT NULL,
                type text NOT NULL,
                time_in_force text NOT NULL,
                price numeric NULL,
                quantity numeric NOT NULL,
                take_profit numeric NULL,
                stop_loss numeric NULL,
                reduce_only boolean NOT NULL,
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
                CONSTRAINT "PK_idea_trade_orders" PRIMARY KEY (trade_id, number),
                CONSTRAINT "FK_idea_trade_orders_idea_trades" FOREIGN KEY (trade_id) REFERENCES idea_trades (id) ON DELETE CASCADE,
                CONSTRAINT "CK_idea_trade_orders_role" CHECK (role IN ('entry', 'close')),
                CONSTRAINT "CK_idea_trade_orders_side" CHECK (side IN ('buy', 'sell')),
                CONSTRAINT "CK_idea_trade_orders_type" CHECK (type IN ('market', 'limit')),
                CONSTRAINT "CK_idea_trade_orders_time_in_force" CHECK (time_in_force IN ('gtc', 'ioc')),
                CONSTRAINT "CK_idea_trade_orders_status" CHECK (status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent', 'filled', 'cancelled', 'rejected'))
            );

            CREATE INDEX "IX_idea_trade_orders_unfinished" ON idea_trade_orders (trade_id, number)
                WHERE status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent');

            CREATE TABLE idea_trade_children (
                exchange_order_id text NOT NULL,
                trade_id uuid NOT NULL,
                leg text NOT NULL,
                filled_quantity numeric NOT NULL,
                filled_value numeric NOT NULL,
                fee numeric NOT NULL,
                finished boolean NOT NULL,
                CONSTRAINT "PK_idea_trade_children" PRIMARY KEY (exchange_order_id),
                CONSTRAINT "FK_idea_trade_children_idea_trades" FOREIGN KEY (trade_id) REFERENCES idea_trades (id) ON DELETE CASCADE,
                CONSTRAINT "CK_idea_trade_children_leg" CHECK (leg IN ('take_profit', 'stop_loss'))
            );

            CREATE INDEX "IX_idea_trade_children_trade" ON idea_trade_children (trade_id);

            CREATE TABLE idea_trade_fills (
                exec_id text NOT NULL,
                trade_id uuid NOT NULL,
                exchange_order_id text NOT NULL,
                quantity numeric NOT NULL,
                price numeric NOT NULL,
                fee numeric NOT NULL,
                fee_asset text NOT NULL,
                executed_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_trade_fills" PRIMARY KEY (exec_id),
                CONSTRAINT "FK_idea_trade_fills_idea_trades" FOREIGN KEY (trade_id) REFERENCES idea_trades (id) ON DELETE CASCADE,
                CONSTRAINT "CK_idea_trade_fills_fee_asset" CHECK (fee_asset IN ('quote', 'base'))
            );

            CREATE INDEX "IX_idea_trade_fills_trade" ON idea_trade_fills (trade_id, executed_at);

            CREATE TABLE idea_trading_settings (
                id integer NOT NULL,
                account_id uuid NULL,
                risk numeric NOT NULL,
                max_amount numeric NOT NULL,
                max_open_trades integer NOT NULL,
                max_total_risk numeric NOT NULL,
                min_risk_reward numeric NOT NULL,
                CONSTRAINT "PK_idea_trading_settings" PRIMARY KEY (id),
                CONSTRAINT "CK_idea_trading_settings_single" CHECK (id = 1),
                CONSTRAINT "FK_idea_trading_settings_exchange_accounts" FOREIGN KEY (account_id) REFERENCES exchange_accounts (id) ON DELETE SET NULL
            );
            """);
    }
}