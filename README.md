# profit-fox

Trading strategy designer, backtesting and live trading.

## Requirements

- .NET SDK 10
- Docker (the local database and the integration tests run in containers)

## Local database

TimescaleDB for development runs from `docker-compose.yml`, listening on `localhost:5432` only:

```bash
docker compose up -d
```

## Configuration

The service reads the database connection string from `ConnectionStrings:MarketData`. In Development it comes from
`appsettings.Development.json` and matches the local database from `docker-compose.yml`; these credentials are
local-only and not a secret. Any other environment gets the connection string from the `ConnectionStrings__MarketData`
environment variable, never from a file in git.

Integration tests run the service in the `Testing` environment: `appsettings.Testing.json` sets their log levels, and
each test gets its own database from Testcontainers, so tests never touch the development database or user secrets.

Market data comes from the public Bybit v5 API through the Bybit.Net client (`CryptoClients.Net`). No API key is
needed and no orders are sent.

## Run

The API and the Worker are two processes on one database. The API creates, cancels and reads backtests and candle
imports; a Worker runs them. Without a Worker they stay `Pending`.

```bash
dotnet run --project backend/src/ProfitFox.Presentation --launch-profile http
```

```bash
dotnet run --project backend/src/ProfitFox.Worker
```

The API applies database migrations on start and listens on `http://localhost:5242`; start it before the Worker, which
never migrates. Any number of Workers may run at once, on this machine or others: a Worker takes one piece of work at a
time under a lease and renews it while it works, so a backtest grid is split into chunks that all Workers compute
together, and the work of a Worker that stopped goes on in another once its lease expires. Candle imports run one at a
time across all Workers. The `Worker` section of the Worker's `appsettings.json` sets the poll interval, the lease and
the combinations in a chunk.

A Worker computes only backtests of its own engine version (`EngineVersion.Current`), and fails unfinished backtests of
an older one when it starts working; their saved results stay readable. A change that changes backtest results raises
the engine version: the engine version test in `ProfitFox.Tests.Unit` fails until it does.

The API is described by the OpenAPI document at `/openapi/v1.json`, and Swagger UI at `/swagger` lets you call it
from the browser. Both are served in every environment, including Production; the API has no authorization yet.

Logs go to the console only: text in Development, one JSON object per line elsewhere. Levels and format are set in
the `Serilog` section of `appsettings*.json` and can be overridden with environment variables such as
`Serilog__MinimumLevel__Default=Debug`.

### Worker on another machine

A Worker on another machine of the local network runs from `docker-compose.worker.yml` in a clone of this repository.
Its settings go to `worker.env` next to the file, which is never committed:

```text
ConnectionStrings__MarketData=Host=192.168.8.111;Port=5432;Database=profitfox;Username=<user>;Password=<password>
Backtests__MaxParallelism=4
```

```bash
docker compose -f docker-compose.worker.yml up -d --build
```

The machines need synchronized clocks (NTP, on by default in Ubuntu): leases are compared by each Worker's clock.

On production, Postgres listens on the LAN interface, `pg_hba.conf` admits only `192.168.8.0/24`, and the password
comes from an environment variable. Deploy the API first, then the Workers, all from the same commit: stop the Workers,
deploy the API, which migrates the database, then start the Workers again.

## Market data API

Start loading hourly `linear` BTCUSDT candles for a period `[from, to)`; `to` defaults to now. Times are ISO 8601 with
an offset or `Z`; a time without one is rejected. The job asks Bybit only for the candles not stored yet and never
changes stored ones. The response is the job, and `Location` points to it; the job may already be `Running` or finished
by the time the response is built:

```bash
curl -i -X POST http://localhost:5242/api/market-data/candle-imports -H 'Content-Type: application/json' -d '{"category":"linear","symbol":"BTCUSDT","interval":"60","from":"2024-01-01T00:00:00Z","to":"2024-01-02T00:00:00Z"}'
```

Check the job: its status goes `Pending` → `Running` → `Succeeded` or `Failed`:

```bash
curl http://localhost:5242/api/market-data/candle-imports/<job id>
```

While Bybit is unavailable, a job tries again after 1, 5, 15, 30 and 60 seconds before it fails. A job of a
stopped Worker continues in another Worker once its lease expires. Retry a `Failed` job; it continues from what is
already stored:

```bash
curl -i -X POST http://localhost:5242/api/market-data/candle-imports/<job id>/retry
```

Read stored candles, oldest first; `limit` is 1000 by default and 5000 at most:

```bash
curl 'http://localhost:5242/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z'
```

Categories are `spot` and `linear`; intervals are Bybit codes `1`, `3`, `5`, `15`, `30`, `60`, `120`, `240`, `360`,
`720`, `D`, `W`, `M`. Prices and volumes are decimal strings.

## Development

```bash
dotnet build backend/ProfitFox.sln
```

```bash
dotnet test backend/ProfitFox.sln
```

```bash
dotnet format backend/ProfitFox.sln
```

Integration tests start TimescaleDB with Testcontainers, so Docker must be running.

The schema is managed by FluentMigrator: migrations live in
`backend/src/ProfitFox.Infrastructure/Persistence/Postgres/FluentMigrator/Migrations` and the service applies them at startup. A new migration is a
class deriving from `ForwardOnlyMigration` with a `[Migration(yyyyMMddnnnn)]` version greater than the last one; its `Up`
runs raw SQL through `Execute.Sql`.
