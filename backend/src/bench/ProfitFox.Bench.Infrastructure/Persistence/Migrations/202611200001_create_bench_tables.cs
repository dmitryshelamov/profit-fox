using FluentMigrator;

namespace ProfitFox.Bench.Infrastructure.Persistence.Migrations;

[Migration(202611200001)]
public sealed class CreateBenchTablesMigration : Migration
{
    // The whole state of the bench (design add-bench-service, decision 2). A hypothesis is only its versions, which are added and
    // never changed. A signal is a row that changes, with versions of its graph that are added. The ledger and the reports are
    // written once: a trigger refuses to change or delete them, so no code and no hand with a connection string can. A run is a row
    // of the queue; the partial unique index keeps one waiting or running run per version, period and kind even when two requests
    // come together.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE hypothesis_versions (
                hypothesis_id text NOT NULL,
                version integer NOT NULL,
                hash text NOT NULL,
                text text NOT NULL,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT hypothesis_versions_pk PRIMARY KEY (hypothesis_id, version),
                CONSTRAINT hypothesis_versions_hash_uq UNIQUE (hypothesis_id, hash),
                CONSTRAINT hypothesis_versions_version_ck CHECK (version >= 1)
            );

            CREATE TABLE signals (
                id uuid NOT NULL,
                name text NOT NULL,
                description text NULL,
                layout jsonb NOT NULL,
                version integer NOT NULL,
                CONSTRAINT signals_pk PRIMARY KEY (id),
                CONSTRAINT signals_name_uq UNIQUE (name)
            );

            CREATE TABLE signal_versions (
                signal_id uuid NOT NULL,
                version integer NOT NULL,
                graph jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT signal_versions_pk PRIMARY KEY (signal_id, version),
                CONSTRAINT signal_versions_signal_id_fk FOREIGN KEY (signal_id) REFERENCES signals (id) ON DELETE CASCADE
            );

            CREATE TABLE runs (
                id text NOT NULL,
                hypothesis_id text NOT NULL,
                version integer NOT NULL,
                period text NOT NULL,
                kind text NOT NULL,
                status text NOT NULL,
                queued_at timestamp with time zone NOT NULL DEFAULT now(),
                started_at timestamp with time zone NULL,
                finished_at timestamp with time zone NULL,
                lease_owner text NULL,
                leased_until timestamp with time zone NULL,
                lost_leases integer NOT NULL DEFAULT 0,
                cancel_requested boolean NOT NULL DEFAULT false,
                pairs_done integer NOT NULL DEFAULT 0,
                pairs_total integer NULL,
                CONSTRAINT runs_pk PRIMARY KEY (id),
                CONSTRAINT runs_version_fk FOREIGN KEY (hypothesis_id, version) REFERENCES hypothesis_versions (hypothesis_id, version),
                CONSTRAINT runs_kind_ck CHECK (kind IN ('verdict', 'parity')),
                CONSTRAINT runs_status_ck CHECK (status IN ('queued', 'running', 'finished'))
            );

            CREATE UNIQUE INDEX runs_active_uq ON runs (hypothesis_id, version, period, kind) WHERE status <> 'finished';

            CREATE INDEX runs_queue_ix ON runs (queued_at) WHERE status <> 'finished';

            CREATE TABLE ledger (
                seq bigint GENERATED ALWAYS AS IDENTITY,
                run_id text NOT NULL,
                time timestamp with time zone NOT NULL,
                hypothesis_id text NOT NULL,
                hash text NOT NULL,
                version integer NOT NULL,
                engine_version integer NOT NULL,
                period text NOT NULL,
                status text NOT NULL,
                data_hash text NULL,
                attempts integer NULL,
                reason text NULL,
                required_t double precision NULL,
                power double precision NULL,
                blocks integer NULL,
                events integer NULL,
                t double precision NULL,
                mean double precision NULL,
                first_half_mean double precision NULL,
                second_half_mean double precision NULL,
                read_seconds double precision NULL,
                corrects text NULL,
                CONSTRAINT ledger_pk PRIMARY KEY (run_id)
            );

            CREATE UNIQUE INDEX ledger_seq_uq ON ledger (seq);

            CREATE INDEX ledger_hypothesis_ix ON ledger (hypothesis_id, version);

            CREATE TABLE reports (
                run_id text NOT NULL,
                text text NOT NULL,
                CONSTRAINT reports_pk PRIMARY KEY (run_id),
                CONSTRAINT reports_run_id_fk FOREIGN KEY (run_id) REFERENCES ledger (run_id)
            );

            CREATE FUNCTION refuse_change() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'rows of % are never changed or deleted: write a new row that corrects the old one', TG_TABLE_NAME
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER ledger_frozen BEFORE UPDATE OR DELETE ON ledger FOR EACH ROW EXECUTE FUNCTION refuse_change();

            CREATE TRIGGER reports_frozen BEFORE UPDATE OR DELETE ON reports FOR EACH ROW EXECUTE FUNCTION refuse_change();
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE reports;
            DROP TABLE ledger;
            DROP FUNCTION refuse_change();
            DROP TABLE runs;
            DROP TABLE signal_versions;
            DROP TABLE signals;
            DROP TABLE hypothesis_versions;
            """);
    }
}
