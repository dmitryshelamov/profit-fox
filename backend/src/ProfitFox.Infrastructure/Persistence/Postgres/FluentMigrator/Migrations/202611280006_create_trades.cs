using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280006)]
public sealed class CreateTradesMigration : Migration
{
    // The trades by ideas, their orders and their fills (change take-idea-trades, design.md, decisions 1, 4, 5 and 12). A
    // trade keeps a snapshot of its idea and the name of its account: the account may be removed — the trade then keeps the
    // name and loses the id — and a closed idea may be removed too. One trade that holds its pair on an account: the partial
    // unique index refuses a second in the states «Ждёт входа», «Открыта», «Закрывается», «Ошибка», so two clicks at once do
    // not make two. A fill is kept once by the id of its execution.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE trades (
                id uuid NOT NULL,
                idea_id uuid NOT NULL,
                symbol text NOT NULL,
                side text NOT NULL CHECK (side IN ('long', 'short')),
                strategy_id uuid NOT NULL,
                strategy_name text NOT NULL,
                strategy_version integer NOT NULL,
                timeframe text NOT NULL,
                idea_moment timestamptz NOT NULL,
                until timestamptz NOT NULL,
                idea_entry numeric NOT NULL,
                account_id uuid NULL REFERENCES exchange_accounts (id) ON DELETE SET NULL,
                account_name text NOT NULL,
                amount numeric NOT NULL,
                leverage integer NOT NULL,
                click_price numeric NOT NULL,
                quantity numeric NOT NULL,
                stop numeric NOT NULL,
                stop_source text NOT NULL CHECK (stop_source IN ('idea', 'limit')),
                target numeric NULL,
                state text NOT NULL CHECK (state IN ('pending', 'open', 'closing', 'closed', 'cancelled', 'error')),
                reason text NULL,
                close_reason text NULL,
                entered numeric NOT NULL DEFAULT 0,
                held numeric NOT NULL DEFAULT 0,
                entry_price numeric NULL,
                exit_price numeric NULL,
                fees numeric NOT NULL DEFAULT 0,
                funding numeric NOT NULL DEFAULT 0,
                result numeric NULL,
                entry_wait_seconds integer NOT NULL,
                stop_attempts integer NOT NULL,
                stop_failures integer NOT NULL DEFAULT 0,
                unrealised numeric NULL,
                marked_at timestamptz NULL,
                flat_since timestamptz NULL,
                created_at timestamptz NOT NULL,
                opened_at timestamptz NULL,
                closed_at timestamptz NULL,
                CONSTRAINT trades_pk PRIMARY KEY (id)
            );
            CREATE UNIQUE INDEX trades_holding_ux ON trades (account_id, symbol) WHERE state IN ('pending', 'open', 'closing', 'error');
            CREATE INDEX trades_idea_ix ON trades (idea_id);
            CREATE INDEX trades_closed_ix ON trades (closed_at DESC) WHERE state IN ('closed', 'cancelled');

            CREATE TABLE trade_orders (
                trade_id uuid NOT NULL REFERENCES trades (id) ON DELETE CASCADE,
                number integer NOT NULL,
                purpose text NOT NULL CHECK (purpose IN ('entry', 'close')),
                reason text NULL,
                quantity numeric NOT NULL,
                status text NOT NULL CHECK (status IN ('pending', 'sent', 'filled', 'rejected', 'cancelled')),
                attempts integer NOT NULL DEFAULT 0,
                next_attempt_at timestamptz NOT NULL,
                exchange_order_id text NULL,
                refusal text NULL,
                created_at timestamptz NOT NULL,
                CONSTRAINT trade_orders_pk PRIMARY KEY (trade_id, number)
            );

            CREATE TABLE trade_fills (
                exec_id text NOT NULL,
                trade_id uuid NOT NULL REFERENCES trades (id) ON DELETE CASCADE,
                side text NOT NULL CHECK (side IN ('long', 'short')),
                price numeric NOT NULL,
                quantity numeric NOT NULL,
                fee numeric NOT NULL,
                kind text NOT NULL CHECK (kind IN ('entry', 'funding', 'exit', 'increase')),
                reason text NULL,
                time timestamptz NOT NULL,
                CONSTRAINT trade_fills_pk PRIMARY KEY (exec_id)
            );
            CREATE INDEX trade_fills_trade_ix ON trade_fills (trade_id, time);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE trade_fills;
            DROP TABLE trade_orders;
            DROP TABLE trades;
            """);
    }
}
