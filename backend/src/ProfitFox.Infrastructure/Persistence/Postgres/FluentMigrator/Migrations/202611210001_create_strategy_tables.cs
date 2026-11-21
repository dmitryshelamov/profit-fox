using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611210001)]
public sealed class CreateStrategyTablesMigration : Migration
{
    // The saved strategies (change add-strategy-constructor, design.md, decision 4), as the signals are kept: a row per strategy
    // that changes and a row per version of its graph that is only added. strategy_signals has a row for each signal node of a
    // version, written in the transaction of the version: its foreign key into signal_versions keeps a signal that a version of
    // a strategy points at from being deleted, also when the delete and the save come together.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategies (
                id uuid NOT NULL,
                name text NOT NULL,
                description text NULL,
                layout jsonb NOT NULL,
                version integer NOT NULL,
                CONSTRAINT strategies_pk PRIMARY KEY (id),
                CONSTRAINT strategies_name_uq UNIQUE (name)
            );

            CREATE TABLE strategy_versions (
                strategy_id uuid NOT NULL,
                version integer NOT NULL,
                graph jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT strategy_versions_pk PRIMARY KEY (strategy_id, version),
                CONSTRAINT strategy_versions_strategy_id_fk FOREIGN KEY (strategy_id) REFERENCES strategies (id) ON DELETE CASCADE
            );

            CREATE TABLE strategy_signals (
                strategy_id uuid NOT NULL,
                version integer NOT NULL,
                node text NOT NULL,
                signal_id uuid NOT NULL,
                signal_version integer NOT NULL,
                CONSTRAINT strategy_signals_pk PRIMARY KEY (strategy_id, version, node),
                CONSTRAINT strategy_signals_version_fk FOREIGN KEY (strategy_id, version)
                    REFERENCES strategy_versions (strategy_id, version) ON DELETE CASCADE,
                CONSTRAINT strategy_signals_signal_fk FOREIGN KEY (signal_id, signal_version)
                    REFERENCES signal_versions (signal_id, version) ON DELETE RESTRICT
            );

            CREATE INDEX strategy_signals_signal_ix ON strategy_signals (signal_id, signal_version);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE strategy_signals;
            DROP TABLE strategy_versions;
            DROP TABLE strategies;
            """);
    }
}
