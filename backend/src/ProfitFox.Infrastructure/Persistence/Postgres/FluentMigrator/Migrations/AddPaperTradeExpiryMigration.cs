using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080003)]
public sealed class AddPaperTradeExpiryMigration : ForwardOnlyMigration
{
    // Decisions 2, 3 and 8: the leg a paper trade closes by at the expiry of its idea, and the exit price of the idea on the outcome of both kinds of
    // trades. The outcomes written before get the exit price of their idea while storage still holds it, so the paper trades their ideas outlived
    // close at expiry on the first minute of the scanner. The image before this one reads and writes none of the new columns.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE idea_paper_trades
                ADD COLUMN expiry_quantity numeric NOT NULL DEFAULT 0,
                ADD COLUMN expiry_value numeric NOT NULL DEFAULT 0,
                ADD COLUMN expiry_fee numeric NOT NULL DEFAULT 0,
                ADD COLUMN idea_exit_price numeric NULL,
                DROP CONSTRAINT "CK_idea_paper_trades_status",
                ADD CONSTRAINT "CK_idea_paper_trades_status" CHECK (status IN ('awaiting_entry', 'open', 'closed_at_target', 'closed_at_stop', 'closed_at_expiry', 'cancelled'));

            ALTER TABLE idea_trades ADD COLUMN idea_exit_price numeric NULL;

            UPDATE idea_paper_trades AS trade SET idea_exit_price = idea.exit_price
            FROM ideas AS idea
            WHERE idea.id = trade.idea_id AND trade.idea_status IS NOT NULL;

            UPDATE idea_trades AS trade SET idea_exit_price = idea.exit_price
            FROM ideas AS idea
            WHERE idea.id = trade.idea_id AND trade.idea_status IS NOT NULL;
            """);
    }
}