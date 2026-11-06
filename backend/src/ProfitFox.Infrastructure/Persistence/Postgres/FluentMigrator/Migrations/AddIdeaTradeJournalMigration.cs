using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070007)]
public sealed class AddIdeaTradeJournalMigration : ForwardOnlyMigration
{
    // Decision 1: the promise of the idea at the entry, written once when the trade is made; a trade made earlier has none.
    // Decision 2: how the idea ended, written by the scanner. Decision 3: the first fill of the entry, taken from the fills for the trades made earlier.
    public override void Up()
    {
        Execute.Sql("""
            ALTER TABLE idea_trades
                ADD COLUMN promised_badge text NULL,
                ADD COLUMN promised_probability numeric NULL,
                ADD COLUMN promised_expectancy numeric NULL,
                ADD COLUMN promised_cases integer NULL,
                ADD COLUMN break_even numeric NULL,
                ADD COLUMN net_risk_reward numeric NULL,
                ADD COLUMN idea_status text NULL,
                ADD COLUMN idea_status_at timestamp with time zone NULL,
                ADD COLUMN idea_result numeric NULL,
                ADD COLUMN opened_at timestamp with time zone NULL,
                ADD CONSTRAINT "CK_idea_trades_promised_badge" CHECK (promised_badge IN ('up', 'flat', 'down')),
                ADD CONSTRAINT "CK_idea_trades_idea_status" CHECK (idea_status IN ('target', 'stop', 'expired'));

            UPDATE idea_trades SET opened_at = (
                SELECT min(fills.executed_at)
                FROM idea_trade_fills AS fills
                JOIN idea_trade_orders AS orders ON orders.trade_id = fills.trade_id AND orders.exchange_order_id = fills.exchange_order_id
                WHERE fills.trade_id = idea_trades.id AND orders.role = 'entry');
            """);
    }
}