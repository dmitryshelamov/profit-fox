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

The production database publishes no port yet (see [Production](#production)): a Worker on another machine comes
with its own change, which opens the port to the local network. Deploy the API first, then the Workers, all from the
same commit: stop the Workers, deploy the API, which migrates the database, then start the Workers again.

### Market scanner and trade ideas

The scanner (`backend/src/ProfitFox.Scanner`) keeps the data of the whole Bybit market and computes the trade ideas; it never
trades. Start it after the API:

```bash
dotnet run --project backend/src/ProfitFox.Scanner
```

Every closed minute, after the market snapshot, it reads the setups of every pair on 5 and 15 minutes, 1 and 4 hours, makes
ideas of the setups that fire and moves the open ones; the setups go into the rows of the snapshot. In the background it
replays the same rules over the stored minutes of the last 90 days, pair by pair and day by day, and every 10 minutes
recomputes the probability of each bucket (setup × type × interval × side × category) from those cases. The replay pauses
while the snapshot comes late, and starts again from scratch when `IdeasEngineVersion.Current` is raised: a change of the
rules raises it, the version test in `ProfitFox.Tests.Unit` fails until it does.

The `Ideas` section of the scanner's `appsettings.json` sets the thresholds of the setups (`Thresholds`, common to all pairs,
`ByInterval` for values of one interval), the stop buffer k and the rules of the zones; `Backtests:DefaultFees` sets the fees and the
slippage the cases are counted with. The live ideas and the replay run by the same thresholds, so a bucket counts every case of the current
version. The starting thresholds are provisional: to try others, put them into `Ideas:Thresholds`, raise
`IdeasEngineVersion.Current`, deploy and wait for the replay of the new version (about a day on production), then compare its
buckets with the version before. Every case keeps the move and the volume it fired with, for analysis.

## Production

Production runs on soyo-01 (`192.168.8.111`) in the local network only: the interface and the API are at
`http://192.168.8.111/` (the market scanner at `/market`). It is the `profit-fox` compose project of
`docker-compose.prod.yml`:

| Service | What it does |
|---|---|
| `db` | TimescaleDB on the volume `profit-fox-db`; no port leaves the server |
| `api` | the API; migrates the database on start; `127.0.0.1:8080` on the server for the deploy to wait for it |
| `worker` | the Worker, with live trading on (`Trading__Enabled=true`): real money |
| `scanner` | the market scanner; it gets no master key |
| `web` | Caddy on port 80: the interface, and `/api` forwarded to the API |

Nothing is built on soyo-01 or anywhere in the LAN: from Russian networks some nuget.org addresses fail at TLS. A
GitHub-hosted runner builds the images, soyo-01 pulls them from the public GHCR packages
`ghcr.io/dmitryshelamov/profit-fox-{api,worker,scanner,web}`, and a self-hosted GitHub Actions runner on soyo-03
(`192.168.8.142`, label `soyo-03`) only starts the deploy on soyo-01 over SSH.

### Versions

A version is a number `N`: tag `vN` on a commit of `main`, and images `:N` built from that commit (label
`org.opencontainers.image.revision`). Every service runs the same `N`. A rebuild of a commit keeps its number.

### Deploy and roll back

1. A merge into `main` runs CI, then `.github/workflows/deploy.yml`. Once CI passes, and Frontend and E2E pass where
   the commit has them, the `build` job tags the commit `v<max+1>` and pushes the four images `:N`.
2. The `images` job checks that the four images `:N` are in GHCR, and the `deploy` job on soyo-03 waits for the owner's approval in the `production` environment: Actions → the Deploy run →
   Review deployments → Approve. The time of the approval is the time of the deploy: live bots stop for it.
3. The job runs `deploy N` on soyo-01 over SSH. `~/profit-fox/deploy.sh` checks on GitHub that tag `vN` is on a commit
   of `main`, downloads `docker-compose.prod.yml` of that commit to `~/profit-fox/releases/N/`, pulls the images and
   checks they are built from that commit. Then it stops the Worker and the scanner, starts the database, updates the
   API and waits up to 120 s for `/openapi/v1.json`, and only then starts the Worker, the scanner and the web of version
   `N`. If the API does not answer, the deploy fails and the Worker and the scanner stay stopped. The running version
   is in `~/profit-fox/current`.

To roll back, or to deploy a version again: Actions → Deploy → Run workflow on `main` with the version number, then
approve. Such a run builds nothing; the images `:N` must be in GHCR. A rollback keeps the data, but the database keeps
the migrations of the newer version: roll back only to a version that works with them.

On the server, `docker compose -p profit-fox ps` lists the services and `docker compose -p profit-fox logs api` shows
the logs of one.

### Scanner settings without a restart

`~/profit-fox/config/scanner/scanner.json` on soyo-01, if it exists, overrides the settings of the scanner, environment
included, and the scanner reads it again on each change. So far only `Scanner:RestRequestsPerSecond` takes effect at once,
from the next second; another setting waits for the next start of the scanner. Create the folder as `soyo` once, before the
first deploy that mounts it, or Docker creates it owned by root:

```bash
mkdir -p ~/profit-fox/config/scanner
```

```bash
nano ~/profit-fox/config/scanner/scanner.json
```

```json
{ "Scanner": { "RestRequestsPerSecond": 15 } }
```

A file that is not valid JSON, or no file, leaves the scanner on the settings of its image (20). A limit of `0` stops every REST
request of the scanner.

### Secrets

The secrets live only in `~/profit-fox/.env.prod` on soyo-01 (mode `600`), never in git, the images or GitHub:

```text
PROFITFOX_DB_PASSWORD=<64 hex characters>
PROFITFOX_MASTER_KEY=<32 bytes in base64>
```

The master key decrypts the exchange keys saved in the database: a new key makes the saved ones unreadable.

### A new deploy.sh

The deploy key can run only `~/profit-fox/deploy.sh`, so a change to `deploy/deploy.sh` reaches the server by hand,
after its PR is merged and before its version is deployed. A change of the command raises `DEPLOY_PROTOCOL` in the
script and in `deploy.yml`, and the old copy refuses the new workflow until it is replaced:

```bash
ssh 192.168.8.111 'curl -fsSL https://raw.githubusercontent.com/dmitryshelamov/profit-fox/main/deploy/deploy.sh -o ~/profit-fox/deploy.sh && chmod 700 ~/profit-fox/deploy.sh'
```

### Setting up the machines

Done once. Steps with `sudo` or in the GitHub settings are the owner's.

The owner, on soyo-03: the runner only runs `ssh`, so `soyo` needs no Docker there. Settings → Actions → Runners →
New self-hosted runner (Linux) in GitHub gives the commands that download the runner to `~/actions-runner` on soyo-03
and a token. Configure it with the label and run it as a service of `soyo`:

```bash
./config.sh --url https://github.com/dmitryshelamov/profit-fox --token <token> --labels soyo-03 --unattended
```

```bash
sudo ./svc.sh install soyo && sudo ./svc.sh start
```

The owner, on soyo-01: `deploy.sh` needs `curl` and `jq`:

```bash
ssh -t 192.168.8.111 'command -v jq || sudo apt-get install -y jq'
```

The owner, in GitHub:

- Settings → Environments → New environment `production`: required reviewer — the owner; deployment branches —
  selected branches, `main`.
- Settings → Actions → General → Approval for running fork pull request workflows from contributors: "Require approval
  for all external contributors".
- Settings → Rules → Rulesets → New tag ruleset for `v*`: restrict updates and deletions, block force pushes. Creation
  stays allowed: the `build` job creates the tags.
- After the first build: each package `profit-fox-api`, `-worker`, `-scanner`, `-web` → Package settings → Change
  visibility → Public. Until then soyo-01 cannot pull the images and the deploy fails at the pull, before anything stops.

The agent, with the owner's approval. On soyo-03, the deploy key and the host key of soyo-01:

```bash
ssh soyo@192.168.8.142 'ssh-keygen -t ed25519 -N "" -C profit-fox-deploy -f ~/.ssh/profit-fox-deploy && ssh-keyscan -t ed25519 192.168.8.111 >> ~/.ssh/known_hosts'
```

On soyo-01, the script and the secrets, generated on the server and never printed:

```bash
ssh 192.168.8.111 'mkdir -p ~/profit-fox && curl -fsSL https://raw.githubusercontent.com/dmitryshelamov/profit-fox/main/deploy/deploy.sh -o ~/profit-fox/deploy.sh && chmod 700 ~/profit-fox/deploy.sh'
```

```bash
ssh 192.168.8.111 'umask 077 && test ! -e ~/profit-fox/.env.prod && printf "PROFITFOX_DB_PASSWORD=%s\nPROFITFOX_MASTER_KEY=%s\n" "$(openssl rand -hex 32)" "$(openssl rand -base64 32)" > ~/profit-fox/.env.prod'
```

The deploy key in `authorized_keys` of soyo-01, allowed only from soyo-03 and only as `deploy.sh`:

```bash
ssh soyo@192.168.8.142 'cat ~/.ssh/profit-fox-deploy.pub' | sed 's|^|from="192.168.8.142",command="/home/soyo/profit-fox/deploy.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-user-rc |' | ssh 192.168.8.111 'cat >> ~/.ssh/authorized_keys'
```

Check from soyo-03: the deploy key without a command and with any other command is refused, and the usual login of
`soyo` still works:

```bash
ssh soyo@192.168.8.142 'ssh -i ~/.ssh/profit-fox-deploy -o IdentitiesOnly=yes soyo@192.168.8.111; ssh -i ~/.ssh/profit-fox-deploy -o IdentitiesOnly=yes soyo@192.168.8.111 id'
```

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
