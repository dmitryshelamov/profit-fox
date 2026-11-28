using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280003)]
public sealed class CreateExchangeAccountsMigration : Migration
{
    // The accounts of the exchange (change add-exchange-accounts, design.md, decision 3): the API key in full, the secret only
    // encrypted — its nonce, ciphertext and tag — and the UID Bybit named. A name is one of its kind without regard to case, and
    // so is a UID. Down removes the table with the secrets.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE exchange_accounts (
                id uuid NOT NULL,
                name text NOT NULL,
                api_key text NOT NULL,
                secret_nonce bytea NOT NULL,
                secret_ciphertext bytea NOT NULL,
                secret_tag bytea NOT NULL,
                uid bigint NOT NULL,
                created_at timestamptz NOT NULL,
                CONSTRAINT exchange_accounts_pk PRIMARY KEY (id)
            );
            CREATE UNIQUE INDEX exchange_accounts_name_ux ON exchange_accounts (lower(name));
            CREATE UNIQUE INDEX exchange_accounts_uid_ux ON exchange_accounts (uid);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE exchange_accounts;");
    }
}
