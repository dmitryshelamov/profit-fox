# profit-fox

## Стенд гипотез

Стенд — это служба: одна база Postgres, API `ProfitFox.Presentation` для фронтенда и CLI, и от одного до N
процессов `ProfitFox.Worker`, которые считают прогоны из очереди в базе. Стенды на разных машинах (Мак, soyo-02)
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
   dotnet run --project backend/src/ProfitFox.Presentation
   ```

3. Worker:

   ```bash
   dotnet run --project backend/src/ProfitFox.Worker
   ```

   Второй Worker — тот же запуск в другом терминале с другим именем:

   ```bash
   Worker__Name=mac-2 dotnet run --project backend/src/ProfitFox.Worker
   ```

4. H0001 в базу (один раз на новую базу; повторная загрузка того же текста новой версии не даёт):

   ```bash
   dotnet run --project backend/src/ProfitFox.Cli -- hypotheses add bench/hypotheses/H0001-funding-short-4h/hypothesis.json
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
dotnet run --project backend/src/ProfitFox.Cli -- run H0001 --wait
```

Команды `bench`:

- `run <id> [--version N] [--period exploration|holdout|new-data] [--wait]` — прогон с вердиктом;
- `parity <id> [--version N] [--wait]` — сверочный прогон вслепую, только разведка;
- `runs` — очередь и последние прогоны;
- `cancel <id прогона>` — снять из очереди или остановить;
- `hypotheses add <файл>` — загрузить гипотезу или её новую версию (текст `hypothesis.json`).

Справка — `bench --help` и `bench <команда> --help`. Адрес стенда — `--bench <адрес>`, иначе `Bench:Url` в `appsettings.json` CLI или переменная `Bench__Url`, по умолчанию
`http://localhost:5240`.

H0001 на 828 парах с soyo-02 считается около 15 минут.

### Как считается гипотеза

Стенд исполняет гипотезу как стратегию: это граф из её сигнала, узла входа со стороной плана и узлов выхода со сроком,
стопом и целью плана. Текст гипотезы, её хэш и версии при этом не меняются.

У стратегии на паре одна позиция. Событие открывает цикл: сделку по плану от входа до выхода по стопу, цели или сроку.
Событие, которое пришло, пока цикл пары не закончился, пропускается: оно не получает случайных входов и не идёт в
статистику. Событие ровно в момент выхода открывает новый цикл. В начале периода позиция закрыта, пары друг от друга не
зависят. Случайный вход считается отдельным циклом и других циклов не видит.

Отчёт прогона показывает число пропущенных событий строкой «Событий пропущено при открытой позиции». У H0001 оно равно
нулю: её события не накладываются, и её результат тот же, что у версии движка 1. Строка журнала и отчёт нового прогона
несут версию движка 2.

### Настройки

API (`backend/src/ProfitFox.Presentation/appsettings.json`) и Worker
(`backend/src/ProfitFox.Worker/appsettings.json`):

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
`docker-compose.prod.yml`, проект `profit-fox` в `~/profit-fox`:

- `db` — Postgres 17 (том `profit-fox-db`, порт наружу не открыт, 1 ГБ);
- `api` — API со страницей, образ `ghcr.io/dmitryshelamov/profit-fox:<N>`, порт `5240`, 512 МБ. При старте
  мигрирует базу; новая база пуста;
- `worker-1`, `worker-2` — образ `ghcr.io/dmitryshelamov/profit-fox-worker:<N>`, имена `soyo-02-1` и `soyo-02-2`, по 1 ГБ.
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
   проверяет метку коммита у обоих образов и поднимает: `db` (ждёт готовности), `api` (ждёт `/health` до 120 с), затем
   `worker-1` и `worker-2`. В конце пишет `~/profit-fox/current` и чистит старые образы. Том базы никогда не удаляется.

### Откат

Тот же деплой прежнего номера: в GitHub, Actions, Deploy, «Run workflow» на ветке `release/0.1.0`, поле `version` — прежнее
`N`; затем Approve. Миграции идут только вперёд: до первой новой версии схемы откат кода безопасен, после неё откат —
отдельное решение. Текущая версия — `~/profit-fox/current` на soyo-02.

### Разовые шаги владельца

0. Ветка по умолчанию репозитория — `release/0.1.0`: `workflow_run` запускает `deploy.yml` только из файла ветки по
   умолчанию.
1. Раннер GitHub Actions для `dmitryshelamov/profit-fox` на soyo-03 с меткой `soyo-03`.
2. Окружение `production` в настройках репозитория: проверяющий — владелец, ветка деплоя только `release/0.1.0`.
3. Ключ деплоя. На soyo-03 под пользователем раннера:

   ```bash
   ssh-keygen -t ed25519 -N '' -f ~/.ssh/profit-fox-deploy
   ```

   и адрес soyo-02 в `~/.ssh/known_hosts`. На soyo-02 в `~/.ssh/authorized_keys` одной строкой, где `<ключ>` — содержимое
   `profit-fox-deploy.pub`:

   ```text
   command="~/profit-fox/deploy.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty,from="192.168.8.142" <ключ>
   ```

4. На soyo-02: `~/profit-fox/.env.prod` с правами `600` и одной строкой `PROFIT_FOX_DB_PASSWORD=<hex>` (например,
   `openssl rand -hex 24`); `deploy/deploy.sh` из репозитория скопировать как `~/profit-fox/deploy.sh` с правом
   исполнения. Нужны `curl`, `jq` и `docker`.
5. Пакеты `profit-fox` и `profit-fox-worker` в GHCR сделать публичными после первой публикации: сервер тянет их
   без входа.
6. В `~/profit-fox-market-data/config/market-data.json` на soyo-02 задать `HistoryRead:MaxConcurrentReads` = 3: два Worker'а и
   предпросмотр стенда делят чтения истории сервиса данных.

Проверка первого деплоя: страница открывается, H0001 загружается с Мака

```bash
dotnet run --project backend/src/ProfitFox.Cli -- --bench http://192.168.8.213:5240 hypotheses add bench/hypotheses/H0001-funding-short-4h/hypothesis.json
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

## Слои

Стенд и бот — одно приложение с одним набором слоёв в `backend/src`; отдельных зон `bench`, `engine` и `bot` больше нет.
Раскладка та же, что в `profit-fox-market-data` (его `docs/conventions.md`): внутри слоя папка называет сначала вид типа,
потом тему (`Runs`, `Hypotheses`, `Signals`, `Graph`, ...), а пространство имён совпадает с папкой. Ссылки идут только
внутрь:

- `ProfitFox.Domain` — расчёт и правила без ввода-вывода. `Entities/<тема>` — сущности: прогон очереди `Run` и сохранённый
  сигнал `SignalDocument` (закрытый конструктор, `Restore` для репозитория, у сигнала ещё `Create` и `Rename`), их общая база
  `Entities/Base/AggregateRoot` и `Events/IDomainEvent`, как в образце; перечисления сущности — в `Entities/<тема>/Enums`.
  `ValueObjects/<тема>` — значения (версия гипотезы `StoredHypothesis`, строка журнала `LedgerEntry`, проверенный граф,
  сигнал, стратегия, минутный путь), перечисления — в `ValueObjects/<тема>/Enums`. `Repositories` — порты хранения
  `IHypothesisRepository`, `ISignalRepository`, `IRunRepository`, `ILedgerRepository`. `Services/<тема>` — то, что считает:
  чтение и проверка графа, узлы, исполнение сигнала и стратегии, позиция пары, сделка, случайные входы, правила журнала и
  вердикта; `Exceptions`. Ни на что не ссылается. Взять прогон, продлить аренду и отменить — по одному оператору
  `IRunRepository` со временем базы, а не «прочитал сущность, изменил, сохранил»: очередь обслуживают несколько Worker'ов.
- `ProfitFox.Application` — команды и запросы MediatR с обработчиками. Команда лежит в `Commands/<тема>/<Имя>/` рядом с
  обработчиком и результатом (`Runs/EnqueueRun/EnqueueRunCommand`, `EnqueueRunCommandHandler`); общий код обработчиков одной
  темы — в `Commands/<тема>/Base`. Запрос и его обработчик — рядом в `Queries/<тема>` (`Runs/GetRunQuery`,
  `GetRunQueryHandler`). Обработчики `internal sealed`, их видит `ProfitFox.Tests.Unit`. `Contracts/<тема>` — то, что
  отдают запросы и команды и показывает API, с именами `<Имя>Internal` (`RunInternal`, `SignalInternal`) и конвертерами из
  домена в `Contracts/<тема>/Converters`. `Ports/MarketData` — порты сервиса данных `IHistorySource` и `IInstrumentCatalog`,
  `Exceptions`.
- `ProfitFox.Infrastructure` — адаптеры портов и регистрация: `InfrastructureRegistrar.AddInfrastructure` собирает
  `Mediatr/MediatrRegistrar`, `Clients/MarketData` (gRPC и REST сервиса данных, его `Protos`, `MarketDataRegistrar`) и
  `Persistence/Postgres` (`PostgresRegistrar`; репозитории `<Имя>Repository` в `Dapper/Repositories/<тема>` со строками
  `Contracts/<Имя>Db`, SQL в `Queries/<Имя>RepositoryQueries`, переводом строки в домен в `Converters/<Имя>DbConverter`;
  миграции в `FluentMigrator/Migrations`, их запуск — `FluentMigrator/BenchMigrations`). Репозитории `internal sealed`,
  их видит `ProfitFox.Tests.Integration`.
- `ProfitFox.Presentation` (API: `Controllers`, `Contracts/<тема>` с запросами и ответами) и `ProfitFox.Worker`
  (`BackgroundServices`) — точки входа. Обе зовут `AddInfrastructure`, контроллеры и служба Worker'а зовут Application
  только через `ISender`; служба шлёт `RunNextCommand` в своей области на каждый шаг. `NodesController` и проверка графа
  в `SignalsController` зовут чистые функции домена напрямую.
- `ProfitFox.Cli` (`bench`) — клиент REST, на остальные проекты не ссылается.

Из образца не взято: папка транзакций и соединение на область (каждый вызов репозитория и так целый в своей транзакции),
обработчики запросов с SQL в Infrastructure (на все запросы отвечают репозитории), общий обработчик исключений Presentation
(тела ошибок разные по маршрутам и записаны в OpenAPI), проверка базы в Infrastructure (`DatabaseHealthCheck` остаётся в
Presentation), NSubstitute (подделки `MemorySignalRepository` и `FakeHistorySource` держат состояние), имена портов
`I<Биржа><Что>Client`, перечисления вместо строк статусов, периода и вида прогона, `Guid` как идентификатор,
`scripts/verify-format.sh` и хук `pre-commit` (design `match-market-data-structure`, решение 8).

Тесты лежат так же, как код, который они проверяют: `ProfitFox.Tests.Unit/Domain/...` и `ProfitFox.Tests.Unit/Application/...`
(тесты обработчиков — `Commands/<тема>/<Обработчик>Tests`, `Queries/<тема>/<Обработчик>Tests`; `Tests.Unit` ссылается только
на Domain, Application и `Tests.Shared`), `ProfitFox.Tests.Integration/Infrastructure/...`,
`ProfitFox.Tests.Integration/Presentation/...`. Интеграционные тесты шлют команды через поставщик служб `AddInfrastructure`
с поддельным сервисом данных (`BenchServices`). Генераторы сущностей — в
`ProfitFox.Tests.Shared/Generators/Domain/Entities/<тема>` (`RunGenerator`, `SignalDocumentGenerator`). Эталоны — в папках
`Fixtures` рядом со своими тестами.

Общие свойства сборки и версии пакетов — в `backend/Directory.Build.props` и `backend/Directory.Packages.props`; `.csproj`
перечисляют только свои пакеты и ссылки. `backend/Directory.Build.targets` держит правило ссылок слоёв: проект может
ссылаться только на перечисленные там проекты, иначе сборка падает. Стиль кода — `.editorconfig` в корне и
`backend/.globalconfig`; нарушение правила с уровнем `error` роняет обычную сборку.

Внутренности расчёта (`internal`: `CompiledGraph`, `CompiledNode`, `InputSource`, `NodeContext`, `Node` и узлы
`Services/Nodes`, `GraphJson`, `GraphValidator`, `StrategyRun`) закрыты от остальных слоёв компилятором. Остальной код Domain
пользуется теми же открытыми входами, что и другие слои: `StrategyGraph`, `SignalExecutor`, `StrategyExecutor`,
`TradeSimulator`, `PairPosition`, `Cycle`, `EntryPlan`.

«Версия движка» — номер версии расчёта, а не проекта: `EngineVersion.Number`. Его поднимают вместе с любым изменением
событий, исходов или вердикта. Эталонный тест `EngineVersionTests` считает две гипотезы, H0001 и H0002 с наложением
событий, и падает, если результат любой из них изменился, а номер нет. Интеграционным тестам нужен Docker.
