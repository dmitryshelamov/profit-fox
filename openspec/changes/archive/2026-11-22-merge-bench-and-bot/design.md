# Design

## Context

Мотивация — в proposal.md. Что есть в коде на момент предложения:

- `backend/src/bench` — шесть проектов `ProfitFox.Bench.*` (Domain 40 файлов, Application 50, Infrastructure 17,
  Presentation 42, Worker 3, Cli 15). Сборка CLI называется `bench`.
- `backend/src/engine` — `ProfitFox.Engine.Strategy` (50 файлов: `Graph`, `Nodes`, `Execution`) и
  `ProfitFox.Engine.Simulation` (12 файлов: `Trades`, `Positions`). На них ссылается только `ProfitFox.Bench.Domain`, а
  `Engine.Simulation` — на `Engine.Strategy`. Сторонних пакетов у них нет, `InternalsVisibleTo` нет: тесты ходят через
  открытые входы.
- `backend/src/bot` — пять проектов-шаблонов с `Class1` и `WeatherForecast`.
- Тесты: `ProfitFox.Tests.Unit` (папки `Bench/*`, `Engine/Strategy/*`, `Engine/Simulation`), `ProfitFox.Tests.Integration`
  (папки `Bench/*`, `Fixtures`), `ProfitFox.Tests.Shared`.
- Миграции базы — FluentMigrator, классы с номером версии; таблица версий хранит номера, а не имена сборок.
- Продовый деплой описан, но не выполнялся: задачи 8.4 и 8.5 изменения `add-bench-service` открыты.

Решения владельца от 2026-11-21:

- стенд и бот — одно приложение: одно API, одна база; расчёт прогонов — в Worker'ах на других хостах;
- одна зона, сейчас, отдельным изменением после слияния `add-strategy-graph`;
- движок вливается в Domain.

## Goals / Non-Goals

**Goals:**

- В решении один набор слоёв: `ProfitFox.Domain`, `Application`, `Infrastructure`, `Presentation`, `Worker`, `Cli`.
- Результаты расчёта те же: дайджесты H0001 и H0002 и версия движка 2 не меняются.
- Каждый промежуточный шаг собирается и проходит все тесты: ошибку переноса видно в том шаге, где она сделана.
- Стенд на Маке работает после изменения без единого ручного шага.

**Non-Goals:**

- Улучшать код по дороге. Перенос и правка логики в одном диффе не проверяются глазами.
- Переименовывать типы, члены и ключи настроек.
- Менять раскладку деплоя по машинам.

## Decisions

### 1. Одна зона, проекты лежат прямо в `backend/src`

```
backend/src/
  ProfitFox.Domain/          расчёт и правила: граф, узлы, сделка, позиция, гипотезы, прогоны, вердикт, журнал
  ProfitFox.Application/     сценарии и порты
  ProfitFox.Infrastructure/  база, сервис данных
  ProfitFox.Presentation/    API и страница
  ProfitFox.Worker/          расчёт прогонов из очереди
  ProfitFox.Cli/             команда bench
```

Ссылки между слоями прежние: Domain ни на что, Application на Domain, Infrastructure на Application, Presentation и Worker
на Application и Infrastructure. Папки решения `/src/bench/`, `/src/engine/`, `/src/bot/` в `ProfitFox.slnx` заменяются одной
`/src/`.

Альтернатива — оставить `Bench.*` и `Bot.*` библиотеками-модулями под общим хостом. Отвергнута владельцем: на каждом шаге
пришлось бы решать, в какой модуль класть общее — сигналы, стратегии, историю.

### 2. Движок — папки Domain, `internal` остаётся `internal`

Папки `Graph`, `Nodes`, `Execution` из `Engine.Strategy` и `Trades`, `Positions` из `Engine.Simulation` переезжают в
`ProfitFox.Domain` целиком, со своей вложенностью. Пересечений имён с папками Domain стенда (`Hypotheses`, `Ledger`,
`MarketData`, `Runs`, `Signals`, `Strategies`, `Verdicts`) нет.

Модификаторы доступа не меняются. Для Application, Infrastructure и Presentation внутренности расчёта остаются закрыты
компилятором, как сейчас. Открываются они только остальному Domain. Правило на ревью: код Domain вне этих пяти папок
пользуется теми же открытыми входами, что и до изменения (`StrategyGraph`, `SignalExecutor`, `StrategyExecutor`,
`TradeSimulator`, `PairPosition`, `Cycle`, `EntryPlan`).

Альтернативы:

- Оставить два проекта движка. Единственная выгода — запрет компилятора; владелец её взвесил и отказался.
- Один проект `ProfitFox.Engine`. Сохраняет запрет, но оставляет пятое понятие в структуре.

### 3. Соответствие имён

| Было | Стало |
|---|---|
| `ProfitFox.Bench.Domain.*` | `ProfitFox.Domain.*` |
| `ProfitFox.Bench.Application.*` | `ProfitFox.Application.*` |
| `ProfitFox.Bench.Infrastructure.*` | `ProfitFox.Infrastructure.*` |
| `ProfitFox.Bench.Presentation.*` | `ProfitFox.Presentation.*` |
| `ProfitFox.Bench.Worker` | `ProfitFox.Worker` |
| `ProfitFox.Bench.Cli.*` | `ProfitFox.Cli.*` |
| `ProfitFox.Engine.Strategy.Graph.*`, `.Nodes.*`, `.Execution` | `ProfitFox.Domain.Graph.*`, `.Nodes.*`, `.Execution` |
| `ProfitFox.Engine.Simulation.Trades`, `.Positions` | `ProfitFox.Domain.Trades`, `.Positions` |

Имена файлов проектов и сборок следуют за именами проектов, кроме CLI: его сборка по-прежнему `bench`. В `.proto`-файлах
сервиса данных меняется только `csharp_namespace`.

### 4. Что не переименовывается

Имена типов и членов, адреса API, имена операций OpenAPI, таблицы и столбцы базы, ключи настроек (`ConnectionStrings:Bench`,
`Bench:Url`, `Worker:Name`, `MarketData:*`), команда `bench` с параметром `--bench`, названия спецификаций OpenSpec.

Типы с `Bench` в имени (`BenchInfrastructure`, `BenchPresentation`, `BenchSchema`, `BenchClient` и другие) сегодня честно
описывают то, что делают: подключают службы стенда и ходят в API стенда. Их переименование — отдельное решение, когда в
приложении появится первая часть не про стенд.

Причина границы: изменение должно проверяться одним взглядом на дифф — «переехало и сменило пространство имён». Каждое
переименование типа добавляет строки, которые нужно читать.

### 5. Тесты переезжают вслед за кодом

| Было | Стало |
|---|---|
| `Tests.Unit/Bench/<папка>` | `Tests.Unit/<папка>` |
| `Tests.Unit/Engine/Strategy/<папка>` | `Tests.Unit/<папка>` (`Graph`, `Nodes`, `Execution`) |
| `Tests.Unit/Engine/Strategy/Fixtures` | `Tests.Unit/Graph/Fixtures` |
| `Tests.Unit/Engine/Simulation` | `Tests.Unit/Trades`; тесты позиции и плана входа — `Tests.Unit/Positions` |
| `Tests.Integration/Bench/<папка>` | `Tests.Integration/<папка>` |
| `Tests.Integration/Bench/BenchDatabase.cs` | `Tests.Integration/Database/` |

Файлы эталонов переезжают вместе со своими тестами и не правятся. В тестах меняются только `namespace`, `using` и строки
путей к папкам эталонов. Содержимое `Tests.Integration/Fixtures`, включая `EngineVersions.json`, не трогается вовсе.

### 6. Порядок переноса: маленькие шаги, каждый зелёный

1. удалить `Bot.*`;
2. перенести и переименовать шесть проектов стенда;
3. влить движок в Domain;
4. перенести папки тестов;
5. сменить продовые имена.

Файлы переносятся через `git mv`, чтобы история строк сохранилась, а дифф показывал переименования. После каждого шага —
сборка и все тесты, затем коммит.

Если после слияния пространств имён имя перестало разрешаться однозначно (например, папка тестов `Graph` рядом с
`ProfitFox.Domain.Graph`), это чинится полным именем или `using`-псевдонимом в месте использования. Типы и папки ради
этого не переименовываются.

### 7. База и миграции не замечают переноса

FluentMigrator ищет миграции в сборке по атрибуту и записывает в таблицу версий номер, а не имя класса или сборки. База
стенда на Маке, уже мигрированная до `202611200001`, после переименования сборки остаётся на той же версии. Проверяется
существующими интеграционными тестами базы и запуском API на базе Мака.

### 8. Продовые имена меняются сейчас, раскладка — нет

| Было | Стало |
|---|---|
| образ `profit-fox-bench` | `profit-fox` |
| образ `profit-fox-bench-worker` | `profit-fox-worker` |
| проект compose `profit-fox-bench` | `profit-fox` |
| служба `bench` | `api` |
| том `profit-fox-bench-db` | `profit-fox-db` |
| база и пользователь `bench` | `profitfox` |
| `BENCH_VERSION`, `BENCH_DB_PASSWORD` | `PROFIT_FOX_VERSION`, `PROFIT_FOX_DB_PASSWORD` |
| каталог `~/profit-fox-bench` | `~/profit-fox` |
| ключ `~/.ssh/profit-fox-bench-deploy` | `~/.ssh/profit-fox-deploy` |

После первого деплоя каждое из этих имён менять дорого: образы в GHCR, том с единственной копией базы, разовые шаги
владельца на двух машинах. Сейчас это правка текста. Ключ настройки `ConnectionStrings:Bench` остаётся (решение 4):
compose передаёт его как `ConnectionStrings__Bench`.

Команда деплоя и её разбор не меняются, `DEPLOY_PROTOCOL` остаётся 1: копии `deploy.sh` на сервере ещё нет.

Службы по-прежнему описаны на одной машине: `db`, `api`, `worker-1`, `worker-2`. Вынос Worker'ов на другие хосты и выбор
машины для API и базы — отдельное изменение.

### 9. Стенд на Маке не трогаем

`docker-compose.yml` для разработки сохраняет проект `profit-fox-bench-dev`, службу `bench-db`, том
`profit-fox-bench-dev-db`, базу и пользователя `bench`. В нём меняется только комментарий. Имена конфигураций в
`.claude/launch.json` (`bench-db`, `bench-api`, `bench-worker`) тоже остаются, меняются пути к проектам.

Причина: в базе Мака лежит журнал прогонов, а журнал определяет число попыток в вердикте. Docker не переименовывает тома;
смена имени тома дала бы пустую базу, а смена имени проекта — осиротевший контейнер на том же порту и ручной шаг.

Альтернатива — переименовать и перенести данные копированием тома. Отвергнута: ручные шаги на машине владельца ради
имени, которое видно только в одном файле.

### 10. Версия движка остаётся 2

Слова «версия движка» и `EngineVersion` остаются: это номер версии расчёта, а не проекта. Изменение не меняет ни события,
ни исходы, ни вердикт, поэтому номер не поднимается, а `EngineVersions.json` не правится.

## Risks / Trade-offs

- [Широкий дифф конфликтует с любой параллельной веткой] → изменение начинается только после слияния `add-strategy-graph`,
  и до его слияния другие ветки с кодом бэкенда не открываются.
- [Перенос незаметно меняет логику] → ожидания тестов не правятся, дайджесты H0001 и H0002 обязаны совпасть, число тестов
  не уменьшается; в диффе тестов допустимы только `namespace`, `using` и пути к эталонам.
- [Имя сборки зашито строкой] → после переноса ищутся строки `ProfitFox.Bench`, `ProfitFox.Engine`, `ProfitFox.Bot`,
  `src/bench`, `src/engine`, `src/bot` по всему репозиторию, кроме `openspec/changes`. Известные места: `ENTRYPOINT` в обоих
  `Dockerfile`, пути `dotnet publish`, `.claude/launch.json`, `README.md`, `deploy.yml`.
- [Схема OpenAPI содержит имя, выведенное из пространства имён] → `schema.d.ts` после `generate:api` обязан остаться прежним;
  если он изменился, это находка, а не повод править фронтенд.
- [Внутренности расчёта видны остальному Domain] → правило решения 2 на ревью.
- [Разовые шаги владельца уже частично сделаны под старыми именами] → задачи 8.4 и 8.5 `add-bench-service` и `README.md`
  получают новые имена; владелец говорит, что из них уже сделано, до первого деплоя.
- [Образы собираются только в `deploy.yml` после push в `release/0.1.0`] → оба `Dockerfile` собираются локально в рамках
  задач, до слияния.
- [Старые рабочие копии в `.claude/worktrees` хранят прежнюю раскладку] → они не входят в репозиторий и не правятся.

## Migration Plan

1. Слияние `add-strategy-graph` в `release/0.1.0`.
2. Изменение применяется в своей ветке шагами решения 6, один пул-реквест в `release/0.1.0`.
3. Схема базы не меняется, миграций нет. Стенд на Маке запускается новыми путями из `README.md` на прежней базе.
4. Push в `release/0.1.0` запускает CI и сборку образов под новыми именами. Деплой ждёт Approve владельца и в это изменение
   не входит.
5. Откат — возврат коммита слияния. Образы с новыми именами в GHCR при откате остаются и ничему не мешают.

## Open Questions

- На какой машине живут API и база и когда Worker'ы выносятся на другие хосты. Решается отдельным изменением; в этом
  раскладка деплоя прежняя.
- Когда переименовывать типы и ключи настроек с `Bench` в имени. Решается, когда появится первая часть приложения не про
  стенд.
