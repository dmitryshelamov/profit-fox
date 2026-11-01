using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611020002)]
public sealed class CreateBotsTableMigration : ForwardOnlyMigration
{
    // No cascade from strategies: a strategy with bots must not be deleted, and the foreign key guards that even against old code.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bots (
                id uuid NOT NULL,
                name text NOT NULL,
                strategy_id uuid NOT NULL,
                parameter_values jsonb NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                "interval" text NOT NULL,
                balance numeric NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_bots" PRIMARY KEY (id),
                CONSTRAINT "FK_bots_strategies" FOREIGN KEY (strategy_id) REFERENCES strategies (id)
            );
            """);
    }
}