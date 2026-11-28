using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280008)]
public sealed class AddAccountFeedMigration : Migration
{
    // How the trading Worker reads an account (change take-idea-trades, design.md, decisions 3 and 12): from which moment it
    // reads the fills by REST, whether the account is on the stream or polled, and since when.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE exchange_accounts
                ADD COLUMN fills_cursor_at timestamptz NULL,
                ADD COLUMN feed text NULL CHECK (feed IN ('stream', 'poll')),
                ADD COLUMN feed_changed_at timestamptz NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE exchange_accounts
                DROP COLUMN feed_changed_at,
                DROP COLUMN feed,
                DROP COLUMN fills_cursor_at;
            """);
    }
}
