using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611040001)]
public sealed class CreateExchangeAccountsTableMigration : ForwardOnlyMigration
{
    // One account of the exchange is stored once per environment. No cascade from bots: an account a bot trades with stays.
    // Only known codes: an unknown environment must never pass for the main network.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE exchange_accounts (
                id uuid NOT NULL,
                name text NOT NULL,
                environment text NOT NULL,
                api_key text NOT NULL,
                secret_nonce bytea NOT NULL,
                secret_ciphertext bytea NOT NULL,
                secret_tag bytea NOT NULL,
                uid bigint NOT NULL,
                categories text[] NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_exchange_accounts" PRIMARY KEY (id),
                CONSTRAINT "UQ_exchange_accounts_uid_environment" UNIQUE (uid, environment),
                CONSTRAINT "CK_exchange_accounts_environment" CHECK (environment IN ('mainnet', 'testnet')),
                CONSTRAINT "CK_exchange_accounts_categories" CHECK (cardinality(categories) > 0 AND categories <@ ARRAY['spot', 'linear'])
            );

            ALTER TABLE bots ADD COLUMN exchange_account_id uuid NULL
                CONSTRAINT "FK_bots_exchange_accounts" REFERENCES exchange_accounts (id);
            """);
    }
}