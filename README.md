# profit-fox

## Стенд гипотез

Стенд — это служба: одна база Postgres, API `ProfitFox.Bench.Presentation` для фронтенда и CLI, и от одного до N
процессов `ProfitFox.Bench.Worker`, которые считают прогоны из очереди в базе. Стенды на разных машинах (Мак, soyo-02)
никак не связаны: у каждого своя база, свои Worker'ы и свой журнал. Спецификации — `openspec/specs/hypothesis-bench`,
`bench-run-queue`, `signal-constructor`, `signal-preview`.

База — единственное хранилище: гипотезы и сигналы с версиями, прогоны, журнал и отчёты. Новая база пуста: из git в неё
ничего не переносится. Файлы в `bench/` — застывшая история прежнего стенда, он их не читает и не пишет; гипотезу из
`bench/hypotheses` загружают в базу как любую другую.

### Стенд на Маке

Нужны .NET 10, Node 22+, Docker и доступ к сервису данных на soyo-02 (`192.168.8.213`, порты 5237 и 5236).

1. База стенда на `127.0.0.1:5435`:

   ```bash
   docker compose up -d --wait bench-db
   ```

2. API на `http://localhost:5240`. При старте мигрирует базу:

   ```bash
   dotnet run --project backend/src/bench/ProfitFox.Bench.Presentation
   ```

3. Worker:

   ```bash
   dotnet run --project backend/src/bench/ProfitFox.Bench.Worker
   ```

   Второй Worker — тот же запуск в другом терминале с другим именем:

   ```bash
   Worker__Name=mac-2 dotnet run --project backend/src/bench/ProfitFox.Bench.Worker
   ```

4. H0001 в базу (один раз на новую базу; повторная загрузка того же текста новой версии не даёт):

   ```bash
   dotnet run --project backend/src/bench/ProfitFox.Bench.Cli -- hypotheses add bench/hypotheses/H0001-funding-short-4h/hypothesis.json
   ```

5. Страница — Vite на `http://localhost:5173`, запросы `/api` уходят на API:

   ```bash
   npm --prefix frontend install
   ```

   ```bash
   npm --prefix frontend run dev
   ```

Все процессы есть в `.claude/launch.json` (`bench-db`, `bench-api`, `bench-worker`, `frontend`). API и Worker, запущенные
превью Claude Code, могут не видеть локальную сеть; для реальных данных их лучше запускать в терминале. После сна Мака их
перезапускают: соединения с сервисом данных остаются мёртвыми.

Начать с чистой базы — удалить её том:

```bash
docker compose down -v
```

### Прогон

В разделе «Сигналы» у каждой гипотезы есть «Прогнать» с выбором периода и «Сверка». Раздел «Прогоны» показывает очередь,
идущие прогоны с ходом по парам и отменой и законченные прогоны; страница прогона — его строку журнала и отчёт.

То же из командной строки:

```bash
dotnet run --project backend/src/bench/ProfitFox.Bench.Cli -- run H0001 --wait
```

Команды `bench`:

- `run <id> [--version N] [--period exploration|holdout|new-data] [--wait]` — прогон с вердиктом;
- `parity <id> [--version N] [--wait]` — сверочный прогон вслепую, только разведка;
- `runs` — очередь и последние прогоны;
- `cancel <id прогона>` — снять из очереди или остановить;
- `hypotheses add <файл>` — загрузить гипотезу или её новую версию (текст `hypothesis.json`).

Адрес стенда — `--bench <адрес>`, иначе `Bench:Url` в `appsettings.json` CLI или переменная `Bench__Url`, по умолчанию
`http://localhost:5240`.

H0001 на 828 парах с soyo-02 считается около 15 минут.

### Настройки

API (`backend/src/bench/ProfitFox.Bench.Presentation/appsettings.json`) и Worker
(`backend/src/bench/ProfitFox.Bench.Worker/appsettings.json`):

- `ConnectionStrings:Bench` — база стенда; для Мака она в `appsettings.Development.json`;
- `MarketData:GrpcUrl`, `MarketData:RestUrl` — адреса сервиса данных;
- `Worker:Name`, `Worker:LeaseDuration` (30 с), `Worker:LeaseRenewal` (10 с), `Worker:PollInterval` (1 с) — Worker.

Их можно задать переменными окружения, например `ConnectionStrings__Bench`, `MarketData__GrpcUrl`, `Worker__Name`.

Типы API для фронтенда генерируются из OpenAPI работающего API:

```bash
npm --prefix frontend run generate:api
```

## Production (soyo-02)

На soyo-02 (`192.168.8.213`) работает свой стенд, не связанный со стендом на Маке: свои база, Worker'ы и журнал. Описание —
`docker-compose.prod.yml`, проект `profit-fox-bench` в `~/profit-fox-bench`:

- `db` — Postgres 17 (том `profit-fox-bench-db`, порт наружу не открыт, 1 ГБ);
- `bench` — API со страницей, образ `ghcr.io/dmitryshelamov/profit-fox-bench:<N>`, порт `5240`, 512 МБ. При старте
  мигрирует базу; новая база пуста;
- `worker-1`, `worker-2` — образ `ghcr.io/dmitryshelamov/profit-fox-bench-worker:<N>`, имена `soyo-02-1` и `soyo-02-2`, по 1 ГБ.
  Берут данные у сервиса данных на этой же машине (`5237`, `5236`).

Страница — `http://192.168.8.213:5240/`. Без авторизации, как остальные службы в локальной сети.

### Как идёт деплой

1. Push в `release/0.1.0` запускает CI (`.github/workflows/ci.yml`: сборка и тесты бэкенда, проверки фронтенда, `shellcheck`
   и тест `deploy/deploy.sh`).
2. После зелёного CI `.github/workflows/deploy.yml` даёт коммиту следующий номер `N` (тег `vN`), собирает на GitHub оба образа
   и кладёт их в GHCR как `:N` с меткой коммита.
3. Задача `deploy` ждёт Approve владельца в окружении `production`, затем раннер на soyo-03 по SSH вызывает на soyo-02
   `deploy <N> 1`.
4. Ключ может запустить только `deploy.sh`. Тот берёт коммит тега `vN` на GitHub, принимает его лишь из `release/0.1.0`,
   проверяет метку коммита у обоих образов и поднимает: `db` (ждёт готовности), `bench` (ждёт `/health` до 120 с), затем
   `worker-1` и `worker-2`. В конце пишет `~/profit-fox-bench/current` и чистит старые образы. Том базы никогда не удаляется.

### Откат

Тот же деплой прежнего номера: в GitHub, Actions, Deploy, «Run workflow» на ветке `release/0.1.0`, поле `version` — прежнее
`N`; затем Approve. Миграции идут только вперёд: до первой новой версии схемы откат кода безопасен, после неё откат —
отдельное решение. Текущая версия — `~/profit-fox-bench/current` на soyo-02.

### Разовые шаги владельца

0. Ветка по умолчанию репозитория — `release/0.1.0`: `workflow_run` запускает `deploy.yml` только из файла ветки по
   умолчанию.
1. Раннер GitHub Actions для `dmitryshelamov/profit-fox` на soyo-03 с меткой `soyo-03`.
2. Окружение `production` в настройках репозитория: проверяющий — владелец, ветка деплоя только `release/0.1.0`.
3. Ключ деплоя. На soyo-03 под пользователем раннера:

   ```bash
   ssh-keygen -t ed25519 -N '' -f ~/.ssh/profit-fox-bench-deploy
   ```

   и адрес soyo-02 в `~/.ssh/known_hosts`. На soyo-02 в `~/.ssh/authorized_keys` одной строкой, где `<ключ>` — содержимое
   `profit-fox-bench-deploy.pub`:

   ```text
   command="~/profit-fox-bench/deploy.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty,from="192.168.8.142" <ключ>
   ```

4. На soyo-02: `~/profit-fox-bench/.env.prod` с правами `600` и одной строкой `BENCH_DB_PASSWORD=<hex>` (например,
   `openssl rand -hex 24`); `deploy/deploy.sh` из репозитория скопировать как `~/profit-fox-bench/deploy.sh` с правом
   исполнения. Нужны `curl`, `jq` и `docker`.
5. Пакеты `profit-fox-bench` и `profit-fox-bench-worker` в GHCR сделать публичными после первой публикации: сервер тянет их
   без входа.
6. В `~/profit-fox-market-data/config/market-data.json` на soyo-02 задать `HistoryRead:MaxConcurrentReads` = 3: два Worker'а и
   предпросмотр стенда делят чтения истории сервиса данных.

Проверка первого деплоя: страница открывается, H0001 загружается с Мака

```bash
dotnet run --project backend/src/bench/ProfitFox.Bench.Cli -- --bench http://192.168.8.213:5240 hypotheses add bench/hypotheses/H0001-funding-short-4h/hypothesis.json
```

и её сверка заканчивается строкой `parity`.

## Конструктор сигналов

Раздел «Сигналы» собирает граф сигнала на холсте и показывает, где он срабатывает на паре в разведке (до 2026-05-15
включительно): график свечей с метками событий и их список.

- `/signals` — сохранённые сигналы и гипотезы стенда;
- `/signals/<имя>` — редактор сигнала с панелью предпросмотра внизу;
- `/signals/new/<id>` — новый сигнал, черновик до первого сохранения;
- `/hypotheses/<id>` — сигнал гипотезы только для чтения, его можно скопировать в новый сигнал;
- `/runs`, `/runs/<id>` — прогоны.

Сигнал хранится в базе стенда: граф в формате `strategy-graph`, расположение узлов и номер версии. Правка графа даёт новую
версию, перемещение узлов — нет. Несохранённые правки живут черновиком в браузере.

Первый предпросмотр пары идёт несколько секунд: столько сервис данных отдаёт свечи и фандинг с начала данных. API держит в
памяти историю последних 5 пар с таймфреймом, поэтому повторный предпросмотр той же пары, в том числе с другим графом,
идёт доли секунды.

## Слои стенда

Стенд в `backend/src/bench` разделён на слои, как зона бота. Ссылки идут только внутрь:

- `ProfitFox.Bench.Domain` — правила без ввода-вывода: гипотеза, периоды, случайные входы, вердикт, правила журнала,
  номер версии движка, события сигнала. Ссылается только на движок.
- `ProfitFox.Bench.Application` — сценарии (прогон, очередь, предпросмотр, библиотека сигналов, список гипотез) и порты:
  `IHistorySource`, `IInstrumentCatalog`, `IHypothesisStore`, `ISignalStore`, `IRunLedger`, `IRunQueue`.
- `ProfitFox.Bench.Infrastructure` — адаптеры портов: gRPC и REST сервиса данных, база (Npgsql, Dapper,
  FluentMigrator).
- `ProfitFox.Bench.Presentation` (API) и `ProfitFox.Bench.Worker` — точки входа, они же собирают зависимости.
- `ProfitFox.Bench.Cli` (`bench`) — клиент REST, на проекты стенда не ссылается.

Номер версии движка (`EngineVersion.Number`) поднимают вместе с любым изменением событий, исходов или вердикта; эталонный
тест `EngineVersionTests` падает, если результаты изменились, а номер нет. Интеграционным тестам нужен Docker.
