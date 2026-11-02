using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611050001)]
public sealed class RemoveTestnetMigration : ForwardOnlyMigration
{
    // Testnet leaves the product: its accounts go, bots that traded with them stay without an account.
    // Every account left is on the main network, so one Bybit account is stored once.
    public override void Up()
    {
        Execute.Sql(
            """
            UPDATE bots SET exchange_account_id = NULL
            WHERE exchange_account_id IN (SELECT id FROM exchange_accounts WHERE environment = 'testnet');

            DELETE FROM exchange_accounts WHERE environment = 'testnet';

            ALTER TABLE exchange_accounts DROP CONSTRAINT "UQ_exchange_accounts_uid_environment";
            ALTER TABLE exchange_accounts DROP CONSTRAINT "CK_exchange_accounts_environment";
            ALTER TABLE exchange_accounts DROP COLUMN environment;
            ALTER TABLE exchange_accounts ADD CONSTRAINT "UQ_exchange_accounts_uid" UNIQUE (uid);
            """);
    }
}