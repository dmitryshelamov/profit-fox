# profit-fox

## Стенд

Стенд — это служба: одна база Postgres, API `ProfitFox.Presentation` для фронтенда и CLI, и от одного до N
процессов `ProfitFox.Worker`, которые считают прогоны стратегий из очереди в базе. Стенды на разных машинах (Мак,
soyo-02) никак не связаны: у каждого своя база и свои Worker'ы. Спецификации — `openspec/specs/strategy-backtest`,
`bench-run-queue`, `strategy-constructor`, `signal-constructor`, `signal-preview`, `strategy-preview`.

База — единственное хранилище: сигналы и стратегии с версиями, прогоны и их результаты. Новая база пуста: из git в неё
ничего не переносится. Таблицы прежнего стенда гипотез (`hypothesis_versions`, `runs`, `ledger`, `reports`) остаются в
базе как есть, код их не читает.

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

4. Страница — Vite на `http://localhost:5173`, запросы `/api` уходят на API:

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

У сохранённой стратегии в редакторе и в меню строки списка есть «Прогнать»: форма запуска с таймфреймами, значениями
параметров (несколько значений дают наборы), началом и концом истории, парами и ставками. Раздел «Прогоны» показывает
очередь, идущие прогоны с ходом по парам и отменой и законченные прогоны; страница прогона — настройки и таблицу наборов,
страница набора — его пары и «Прогнать на остатке истории».

То же из командной строки:

```bash
dotnet run --project backend/src/ProfitFox.Cli -- run funding-short --timeframe 4h --param threshold=0.0005,0.001 --wait
```

Команды `bench`:

- `run <стратегия> [--version N] [--timeframe 1h,4h] [--param имя=v1,v2]… [--from дата] [--until дата] [--pairs A,B]
  [--wait]` — прогон стратегии; `--timeframe` — таймфреймы через запятую из `5m`, `15m`, `1h`, `4h`, `1d`, по умолчанию
  `4h`; с `--wait` печатает таблицу наборов;
- `runs` — очередь и последние прогоны;
- `cancel <id прогона>` — снять из очереди или остановить.

Справка — `bench --help` и `bench <команда> --help`. Адрес стенда — `--bench <адрес>`, иначе `Bench:Url` в `appsettings.json` CLI или переменная `Bench__Url`, по умолчанию
`http://localhost:5240`.

### Как считается прогон

Прогон считает сохранённую версию стратегии на истории каждой пары: свечи таймфреймов и фандинг пары читаются один раз
для всех наборов. У стратегии на паре одна позиция; заявка, пришедшая при открытой позиции, пропускается. У стратегии
вида «категория идей» минутные свечи читаются только в неделях с заявками, и на каждую заявку берётся 20 случайных
входов той же недели: разница со случайным входом — подсказка, а не вердикт. У стратегии вида «для бота» минутные свечи
читаются по всей истории прогона. Прогон несёт номер версии движка (сейчас 3).

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

На soyo-02 (`192.168.8.213`) работает свой стенд, не связанный со стендом на Маке: свои база и Worker'ы. Описание —
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

Проверка первого деплоя: страница открывается, сохранённая стратегия `funding-short` ставится в очередь с Мака

```bash
dotnet run --project backend/src/ProfitFox.Cli -- --bench http://192.168.8.213:5240 run funding-short --timeframe 4h --wait
```

и прогон заканчивается таблицей наборов.

## Конструктор сигналов

Раздел «Сигналы» собирает граф сигнала на холсте и показывает, где он срабатывает на паре в разведке (до начала текущих
суток UTC): график свечей с метками событий и их список.

- `/signals` — сохранённые сигналы;
- `/signals/<имя>` — редактор сигнала с панелью предпросмотра внизу;
- `/signals/new/<id>` — новый сигнал, черновик до первого сохранения;
- `/runs`, `/runs/<id>`, `/runs/<id>/sets/<n>` — прогоны, прогон и набор прогона.

Сигнал хранится в базе стенда: граф в формате `strategy-graph`, расположение узлов и номер версии. Правка графа даёт новую
версию, перемещение узлов — нет. Несохранённые правки живут черновиком в браузере.

Первый предпросмотр пары идёт несколько секунд: столько сервис данных отдаёт свечи и фандинг с начала данных. API держит в
памяти историю последних 5 пар с таймфреймом, поэтому повторный предпросмотр той же пары, в том числе с другим графом,
идёт доли секунды.

## Конструктор стратегий

Стратегия — граф, который открывает и закрывает позицию: узел «Вход» (`enter`) с шортом или лонгом на событии, узел
«Закрыть через» (`close-after`) со сроком и, по желанию, «Стоп» и «Цель». Раздел «Стратегии» собирает такой граф на том же
холсте, что у сигналов, хранит его в базе с версиями (как сигнал) и проверяет без истории.

- `/strategies` — сохранённые стратегии;
- `/strategies/<имя>` — редактор стратегии;
- `/strategies/new/<id>` — новая стратегия, черновик до первого сохранения;
- `/strategies/<имя>/run` — форма запуска прогона.

У стратегии есть вид, его выбирают при создании, и он не меняется: «для бота» — любой граф стратегии, «категория идей» —
вход узлами «Вход» по событию, срок «Закрыть через», стоп и цель по желанию, без заявок по условию и без чтения состояния
позиции.

Сохранённый сигнал входит в стратегию узлом сигнала: он ссылается на сигнал и номер его версии, его входы — параметры этой
версии, выход — событие `event`. Вход без провода и без значения берёт значение параметра из сигнала — оно видно на входе
серым. Перед расчётом узел сигнала заменяется узлами графа своей версии, так что считается стратегия так же, как граф,
собранный вручную.

Версия сигнала в стратегии закреплена: правка сигнала даёт ему новую версию, а стратегия по-прежнему ссылается на старую и
считает по ней. Узел показывает, что есть версия новее, и кнопка «Обновить до версии N» в его карточке переводит ссылку —
это правка графа, после сохранения у стратегии новая версия. Переименовать сигнал можно: стратегия ссылается на него по
идентификатору и показывает новое имя. Удалить сигнал, на который ссылается хоть одна версия стратегии, нельзя — отказ
называет стратегии; удаление стратегии снимает запрет.

Стратегия H0001 — шорт на первом за день пересечении фандингом порога, выход через четыре часа:

1. В разделе «Сигналы» нажать «Новый сигнал», вставить в карточке «JSON» граф сигнала H0001 (он записан в
   `backend/tests/ProfitFox.Tests.Unit/Domain/Services/Graph/Graphs.cs`, константа `H0001`) и сохранить под именем
   `funding-high`.
2. В строке `funding-high` или в его редакторе нажать «Создать стратегию»: откроется новая стратегия с узлом сигнала
   `funding-high`, проводом от его `event` ко входу `when` узла «Вход» и узлом «Закрыть через».
3. У узла «Вход» выбрать `side` = `short`, у узла «Закрыть через» вписать срок `4h`. Счётчик «Ошибки» станет 0.
4. Нажать «Сохранить» и дать имя `funding-short`: стратегия появится в разделе «Стратегии» с версией 1.

Новую стратегию можно собрать и с нуля: «Новая стратегия» спрашивает вид; «категория идей» начинается с узлов «Вход» и
«Закрыть через», «для бота» — с узла «Позиция», а сохранённые сигналы лежат в палитре (кнопка «+») в группе «Сигналы».

## Слои

Стенд и бот — одно приложение с одним набором слоёв в `backend/src`; отдельных зон `bench`, `engine` и `bot` больше нет.
Раскладка та же, что в `profit-fox-market-data` (его `docs/conventions.md`): внутри слоя папка называет сначала вид типа,
потом тему (`Backtests`, `Runs`, `Signals`, `Graph`, ...), а пространство имён совпадает с папкой. Ссылки идут только
внутрь:

- `ProfitFox.Domain` — расчёт и правила без ввода-вывода. `Entities/<тема>` — сущности: прогон стратегии `Backtest`,
  сохранённые сигнал `SignalDocument` и стратегия `StrategyDocument`, их общая база `Entities/Base/AggregateRoot` и
  `Events/IDomainEvent`, как в образце; перечисления сущности — в `Entities/<тема>/Enums`. `ValueObjects/<тема>` — значения
  (настройки и наборы прогона, числа набора, проверенный граф, сигнал, стратегия, минутный путь), перечисления — в
  `ValueObjects/<тема>/Enums`. `Repositories` — порты хранения `ISignalRepository`, `IStrategyRepository`,
  `IBacktestRepository`. `Services/<тема>` — то, что считает: чтение и проверка графа, узлы, исполнение сигнала и
  стратегии, позиция пары, сделка, случайные входы, наборы и расчёт прогона на паре; `Exceptions`. Ни на что не
  ссылается. Взять прогон, продлить аренду и отменить — по одному оператору `IBacktestRepository` со временем базы, а не
  «прочитал сущность, изменил, сохранил»: очередь обслуживают несколько Worker'ов.
- `ProfitFox.Application` — команды и запросы MediatR с обработчиками. Команда лежит в `Commands/<тема>/<Имя>/` рядом с
  обработчиком и результатом (`Backtests/EnqueueBacktest/EnqueueBacktestCommand`, `EnqueueBacktestCommandHandler`); общий код обработчиков одной
  темы — в `Commands/<тема>/Base`. Запрос и его обработчик — рядом в `Queries/<тема>` (`Backtests/GetBacktestQuery`,
  `GetBacktestQueryHandler`). Обработчики `internal sealed`, их видит `ProfitFox.Tests.Unit`. `Contracts/<тема>` — то, что
  отдают запросы и команды и показывает API, с именами `<Имя>Internal` (`BacktestInternal`, `SignalInternal`) и конвертерами из
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
`ProfitFox.Tests.Shared/Generators/Domain/Entities/<тема>` (`BacktestGenerator`, `SignalDocumentGenerator`). Эталоны — в папках
`Fixtures` рядом со своими тестами.

Общие свойства сборки и версии пакетов — в `backend/Directory.Build.props` и `backend/Directory.Packages.props`; `.csproj`
перечисляют только свои пакеты и ссылки. `backend/Directory.Build.targets` держит правило ссылок слоёв: проект может
ссылаться только на перечисленные там проекты, иначе сборка падает. Стиль кода — `.editorconfig` в корне и
`backend/.globalconfig`; нарушение правила с уровнем `error` роняет обычную сборку.

Внутренности расчёта (`internal`: `CompiledGraph`, `CompiledNode`, `InputSource`, `NodeContext`, `Node` и узлы
`Services/Nodes`, `GraphJson`, `GraphValidator`, `StrategyRun`) закрыты от остальных слоёв компилятором. Остальной код Domain
пользуется теми же открытыми входами, что и другие слои: `StrategyGraph`, `SignalExecutor`, `StrategyExecutor`,
`CycleRun`, `PairPosition`, `Cycle`, `EntryPlan`.

«Версия движка» — номер версии расчёта, а не проекта: `EngineVersion.Number`. Его поднимают вместе с любым изменением
сделок, случайных входов или чисел результата. Эталонный тест `EngineVersionTests` считает прогоны двух стратегий, H0001
и H0002 с наложением заявок, и падает, если результат любой из них изменился, а номер нет. Интеграционным тестам нужен Docker.
