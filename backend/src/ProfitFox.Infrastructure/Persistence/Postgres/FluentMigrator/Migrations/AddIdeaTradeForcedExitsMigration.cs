using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611090002)]
public sealed class AddIdeaTradeForcedExitsMigration : ForwardOnlyMigration
{
    // Decisions 3 and 4 of recognize-liquidations-in-idea-trades: the legs of the liquidation and of the auto-deleveraging, the statuses they close a
    // trade with, and the shares of one execution of the exchange between the trades of its side. The trades saved before took nothing by either leg;
    // the image before this one reads and writes none of it.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_trades
                ADD COLUMN liquidation_quantity numeric NOT NULL DEFAULT 0,
                ADD COLUMN liquidation_value numeric NOT NULL DEFAULT 0,
                ADD COLUMN liquidation_fee numeric NOT NULL DEFAULT 0,
                ADD COLUMN adl_quantity numeric NOT NULL DEFAULT 0,
                ADD COLUMN adl_value numeric NOT NULL DEFAULT 0,
                ADD COLUMN adl_fee numeric NOT NULL DEFAULT 0;

            ALTER TABLE idea_trades DROP CONSTRAINT "CK_idea_trades_status";
            ALTER TABLE idea_trades ADD CONSTRAINT "CK_idea_trades_status"
                CHECK (status IN ('awaiting_entry', 'open', 'closed_at_target', 'closed_at_stop', 'closed_manually', 'liquidated', 'closed_by_adl', 'cancelled', 'failed'));

            CREATE TABLE idea_trade_forced_shares (
                exec_id text NOT NULL,
                trade_id uuid NOT NULL,
                kind text NOT NULL,
                quantity numeric NOT NULL,
                value numeric NOT NULL,
                fee numeric NOT NULL,
                executed_at timestamp with time zone NOT NULL,
                applied boolean NOT NULL,
                CONSTRAINT "PK_idea_trade_forced_shares" PRIMARY KEY (exec_id, trade_id),
                CONSTRAINT "FK_idea_trade_forced_shares_idea_trades" FOREIGN KEY (trade_id) REFERENCES idea_trades (id) ON DELETE CASCADE,
                CONSTRAINT "CK_idea_trade_forced_shares_kind" CHECK (kind IN ('liquidation', 'adl'))
            );
            """);
    }
}