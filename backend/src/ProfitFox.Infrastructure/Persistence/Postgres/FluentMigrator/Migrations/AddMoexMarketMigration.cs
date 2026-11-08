using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080008)]
public sealed class AddMoexMarketMigration : ForwardOnlyMigration
{
    // The third market, moex on T-Invest (decisions 2, 4 and 12): an account belongs to an exchange, a T-Invest account has the number
    // of its brokerage account instead of a key and a uid, and every account of the exchange is stored once; the border of the
    // coverage of a series of candles tells which part of a period storage holds whole.
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE exchange_accounts ADD COLUMN exchange text NOT NULL DEFAULT 'bybit';
            ALTER TABLE exchange_accounts ALTER COLUMN exchange DROP DEFAULT;
            ALTER TABLE exchange_accounts ADD CONSTRAINT "CK_exchange_accounts_exchange" CHECK (exchange IN ('bybit', 'tinvest'));
            ALTER TABLE exchange_accounts ALTER COLUMN api_key DROP NOT NULL;
            ALTER TABLE exchange_accounts ALTER COLUMN uid DROP NOT NULL;
            ALTER TABLE exchange_accounts ADD COLUMN account_id text NULL;
            ALTER TABLE exchange_accounts DROP CONSTRAINT "CK_exchange_accounts_categories";
            ALTER TABLE exchange_accounts ADD CONSTRAINT "CK_exchange_accounts_categories"
                CHECK (cardinality(categories) > 0 AND categories <@ ARRAY['spot', 'linear', 'moex']);
            ALTER TABLE exchange_accounts DROP CONSTRAINT "UQ_exchange_accounts_uid";
            CREATE UNIQUE INDEX "UQ_exchange_accounts_bybit_uid" ON exchange_accounts (uid) WHERE exchange = 'bybit';
            CREATE UNIQUE INDEX "UQ_exchange_accounts_tinvest_account_id" ON exchange_accounts (account_id) WHERE exchange = 'tinvest';

            CREATE TABLE candle_coverage (
                category text NOT NULL,
                symbol text NOT NULL,
                "interval" text NOT NULL,
                covered_from timestamp with time zone NOT NULL,
                covered_until timestamp with time zone NOT NULL,
                CONSTRAINT "PK_candle_coverage" PRIMARY KEY (category, symbol, "interval")
            );
            """);
    }
}