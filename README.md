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

```bash
dotnet run --project backend/src/ProfitFox.Presentation --launch-profile http
```

The service applies database migrations on start and listens on `http://localhost:5242`.

The API is described by the OpenAPI document at `/openapi/v1.json`, and Swagger UI at `/swagger` lets you call it
from the browser. Both are served in every environment, including Production; the API has no authorization yet.

Logs go to the console only: text in Development, one JSON object per line elsewhere. Levels and format are set in
the `Serilog` section of `appsettings*.json` and can be overridden with environment variables such as
`Serilog__MinimumLevel__Default=Debug`.

## Market data API

Start loading hourly `linear` BTCUSDT candles for a period `[from, to)`; `to` defaults to now. Times are ISO 8601 with
an offset or `Z`; a time without one is rejected. The response is the job, and `Location` points to it; the job may
already be `Running` or finished by the time the response is built:

```bash
curl -i -X POST http://localhost:5242/api/market-data/candle-imports -H 'Content-Type: application/json' -d '{"category":"linear","symbol":"BTCUSDT","interval":"60","from":"2024-01-01T00:00:00Z","to":"2024-01-02T00:00:00Z"}'
```

Check the job: its status goes `Pending` → `Running` → `Succeeded` or `Failed`:

```bash
curl http://localhost:5242/api/market-data/candle-imports/<job id>
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
