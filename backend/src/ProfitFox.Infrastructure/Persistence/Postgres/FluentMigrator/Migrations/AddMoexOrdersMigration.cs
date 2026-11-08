using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611080009)]
public sealed class AddMoexOrdersMigration : ForwardOnlyMigration
{
    // Decision 3 of add-moex-live-bots: the request id a T-Invest order goes with, written under the client id of the order before the
    // first send, so every send of the order goes with it and an order of the exchange is told by it. The exchange id comes after the send.
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE moex_orders (
                client_id text NOT NULL,
                account_id text NOT NULL,
                request_id uuid NOT NULL,
                exchange_order_id text NULL,
                instrument_uid text NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_moex_orders" PRIMARY KEY (client_id),
                CONSTRAINT "UQ_moex_orders_request_id" UNIQUE (request_id)
            );
            """);
    }
}