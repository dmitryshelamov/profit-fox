using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080005)]
public sealed class CreateLiquidationsTablesMigration : ForwardOnlyMigration
{
    // The exchange keeps no history of liquidations, so every event is kept for good as the stream gives it: daily chunks, so a chunk
    // is compressed each day once it is 7 days old; the unique index makes an event the stream sends again after a reconnect one row.
    // A break of the stream is a row of stream_breaks, so a reader tells a gap from a quiet market.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE liquidations (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                side text NOT NULL,
                price numeric NOT NULL,
                quantity numeric NOT NULL
            );

            SELECT create_hypertable('liquidations', by_range('time', INTERVAL '1 day'));

            CREATE UNIQUE INDEX "UX_liquidations_event" ON liquidations (category, symbol, "time", side, price, quantity);

            ALTER TABLE liquidations SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'category, symbol',
                timescaledb.compress_orderby = '"time"'
            );

            SELECT add_compression_policy('liquidations', INTERVAL '7 days');

            CREATE TABLE stream_breaks (
                stream text NOT NULL,
                lost_at timestamp with time zone NOT NULL,
                restored_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_stream_breaks" PRIMARY KEY (stream, lost_at)
            );
            """);
    }
}