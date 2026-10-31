using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611010002)]
public sealed class CreateStrategyLibraryTablesMigration : ForwardOnlyMigration
{
    // One revision row guards the whole library: every save checks and bumps it in the same transaction.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategy_library (
                id integer NOT NULL,
                revision bigint NOT NULL,
                CONSTRAINT "PK_strategy_library" PRIMARY KEY (id)
            );

            INSERT INTO strategy_library (id, revision) VALUES (1, 0);

            CREATE TABLE strategies (
                id uuid NOT NULL,
                name text NOT NULL,
                definition jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_strategies" PRIMARY KEY (id)
            );

            CREATE TABLE strategy_composite_nodes (
                id uuid NOT NULL,
                name text NOT NULL,
                definition jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_strategy_composite_nodes" PRIMARY KEY (id)
            );
            """);
    }
}