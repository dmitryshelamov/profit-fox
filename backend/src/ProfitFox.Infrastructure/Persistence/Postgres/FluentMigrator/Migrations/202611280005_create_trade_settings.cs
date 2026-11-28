using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611280005)]
public sealed class CreateTradeSettingsMigration : Migration
{
    // The settings of the risk module (change take-idea-trades, design.md, decision 12): one row, the eight limits empty until
    // the owner sets them, and the three numbers of time and attempts empty until saved — read then as their defaults. An
    // account of the settings that is removed leaves the setting empty.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE trade_settings (
                id smallint NOT NULL,
                default_account_id uuid NULL REFERENCES exchange_accounts (id) ON DELETE SET NULL,
                default_amount numeric NULL,
                leverage integer NULL,
                max_amount numeric NULL,
                max_open_trades integer NULL,
                max_total_amount numeric NULL,
                max_loss_percent numeric NULL,
                max_day_loss numeric NULL,
                expiry_margin_minutes integer NULL,
                entry_wait_seconds integer NULL,
                stop_attempts integer NULL,
                saved_at timestamptz NOT NULL,
                CONSTRAINT trade_settings_pk PRIMARY KEY (id),
                CONSTRAINT trade_settings_one_ck CHECK (id = 1)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE trade_settings;");
    }
}
