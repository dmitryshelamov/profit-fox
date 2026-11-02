using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611050003)]
public sealed class AddLiveBotSessionFailuresMigration : ForwardOnlyMigration
{
    // The failures in a row of the Workers to take a live bot up: the third one fails the bot.
    public override void Up()
    {
        Execute.Sql("ALTER TABLE live_bots ADD COLUMN session_failures integer NOT NULL DEFAULT 0;");
    }
}