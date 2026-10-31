# profit-fox

Trading strategy designer, backtesting and live trading.

## Requirements

- .NET SDK 10
- Docker (the local database and the integration tests run in containers)

## Local database

TimescaleDB for development runs from `docker-compose.yml`. The password comes from `.env`, which is not committed:

```bash
cp .env.example .env
```

Set `POSTGRES_PASSWORD` in `.env`, then start the database:

```bash
docker compose up -d
```

## Configuration

The service reads the database connection string from `ConnectionStrings:MarketData`. Keep it out of git:
either set the `ConnectionStrings__MarketData` environment variable or store it in user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:MarketData" "Host=localhost;Port=5432;Database=profitfox;Username=profitfox;Password=<POSTGRES_PASSWORD from .env>" --project backend/src/ProfitFox.Presentation
```

Market data comes from the public Bybit v5 API through the Bybit.Net client (`CryptoClients.Net`). No API key is
needed and no orders are sent.

## Run

```bash
dotnet run --project backend/src/ProfitFox.Presentation --launch-profile http
```

The service applies database migrations on start and listens on `http://localhost:5242`.

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
