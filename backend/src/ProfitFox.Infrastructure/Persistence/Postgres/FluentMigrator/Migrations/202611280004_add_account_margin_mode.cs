using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280004)]
public sealed class AddAccountMarginModeMigration : Migration
{
    // The margin mode of an account (change add-exchange-accounts, design.md, decision 11): as Bybit named it at the addition
    // or at the last «Показать»; empty for an account added before it was read.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE exchange_accounts ADD COLUMN margin_mode text;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE exchange_accounts DROP COLUMN margin_mode;");
    }
}
