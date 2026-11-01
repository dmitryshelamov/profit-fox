using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611020001)]
public sealed class CreateStrategyVersionsTableMigration : ForwardOnlyMigration
{
    // Existing strategies get version 1 from a startup step: SQL cannot expand a graph.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategy_versions (
                strategy_id uuid NOT NULL,
                number integer NOT NULL,
                snapshot jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_strategy_versions" PRIMARY KEY (strategy_id, number),
                CONSTRAINT "FK_strategy_versions_strategies" FOREIGN KEY (strategy_id) REFERENCES strategies (id) ON DELETE CASCADE
            );
            """);
    }
}