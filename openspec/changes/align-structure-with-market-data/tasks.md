# Tasks

Команды бэкенда запускаются из `backend/`: `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx`;
интеграционным тестам нужен Docker. Каждая группа кончается сборкой без ошибок и без предупреждений сверх базы, зелёным
прогоном всех тестов с числом, равным базе, и коммитом группы (коммит — по слову владельца, см. «Открыто после применения»).
Во всём изменении не правятся ожидания тестов, файлы эталонов, имена типов и членов и логика кода. Файлы переносятся через
`git mv`.

«Сверка переноса» в проверках ниже — скрипт, который для каждого `.cs` из `backend` на базовом дереве сравнивает его с
файлом на новом месте после удаления строк `using`, `namespace` и пустых строк над первой строкой кода; допустимые
расхождения — только перечисленные в design.md, решения 3, 10 и 11.

## База

Снято 2026-11-21 на `f5bc916` до правок, Мак владельца, .NET SDK 10.0.302, Docker 29.9.3.

- `git merge-base --is-ancestor f5bc916 HEAD` — успех.
- `dotnet test ProfitFox.slnx`: unit — 251 прошёл из 251, интеграционные — 162 из 162, упавших и пропущенных нет.
- `dotnet build ProfitFox.slnx --configuration Release`: 0 предупреждений, 0 ошибок.
- Проверка однострочных `<summary>` из `ci.yml`: совпадений нет.
- SHA-256 `frontend/src/api/generated/schema.d.ts`:
  `56f993fcfd20a65f52df17cb56bfe0460173333c55cfba7913250153a631a3c5`.
- Повторяющиеся имена типов в решении (с кодом, сгенерированным из `.proto`): `Program` (три проекта), `RunLedgerTests` (два
  проекта тестов), `TimeRange` (Domain и `.proto`; где нужны оба, тип назван полным именем или псевдонимом).

## 1. Предложение

- [x] 1.1 Записать раздел «База» и это изменение OpenSpec. Проверить:
  `openspec validate align-structure-with-market-data --strict` проходит.

## 2. Файлы сборки

- [x] 2.1 Поправить замечания решения 3 (`dotnet format style --diagnostics IDE0005 IDE0007 IDE0008`, два тега `<param>`).
  Проверить: сборка без файлов сборки — 0 предупреждений; дифф содержит только строки `using`, объявления `var`/явного типа и
  два тега `<param>`.
- [x] 2.2 `backend/Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`, `backend/.globalconfig`,
  `.editorconfig` в корне по design.md, решения 1 и 2; из всех `.csproj` убрать общие свойства и атрибуты `Version`; файлы
  — в папку `/common/` `ProfitFox.slnx`. Проверить: сборка — 0 предупреждений, 0 ошибок; тесты — 251 и 162;
  `dotnet list ProfitFox.slnx package` даёт те же 25 ссылок с теми же версиями, что `.csproj` на `f5bc916`.
- [x] 2.3 Правило ссылок слоёв на деле (решение 2). Проверить: временная ссылка `ProfitFox.Domain` → `ProfitFox.Cli` роняет
  сборку сообщением `ProfitFox.Domain may reference only [], not ProfitFox.Cli. The allowed references are listed in
  backend/Directory.Build.targets.`; после отката `ProfitFox.Domain.csproj` прежний.

## 3. Domain

- [x] 3.1 Перенести файлы `ProfitFox.Domain` по таблице design.md, решение 5; `namespace` — за папкой; `using` во всех
  проектах — по решению 11. Проверить: сборка — 0 предупреждений; тесты — 251 и 162; `git diff -M` — 102 переименования;
  сверка переноса — только строки с полным именем в `CliTests`, `HypothesesTests`, `RunsTests`, `RunReportTests`; модификаторы
  доступа не изменились (решение 9: `internal`-типы Domain — ровно список решения).

## 4. Application

- [x] 4.1 Перенести файлы `ProfitFox.Application` по таблице design.md, решение 6. Проверить: сборка — 0 предупреждений;
  тесты — 251 и 162; сверка переноса пуста.

## 5. Infrastructure

- [x] 5.1 Перенести файлы `ProfitFox.Infrastructure` по таблице design.md, решение 7, вместе с `.proto`; поправить
  `csharp_namespace` и путь `Protobuf Include`. Проверить: сборка — 0 предупреждений; тесты — 251 и 162; сверка переноса —
  только строки `…Protos.*` в `FakeCandleFeed`, `FakeFundingFeed`, `PreviewTests` ×2; дифф каждого `.proto` — одна строка
  `csharp_namespace`.

## 6. Presentation, Worker, Cli

- [x] 6.1 Перенести файлы `ProfitFox.Presentation`, `ProfitFox.Worker`, `ProfitFox.Cli` по таблицам design.md, решение 8.
  Проверить: сборка — 0 предупреждений; тесты — 251 и 162; сверка переноса пуста.

## 7. Тесты

- [x] 7.1 Перенести файлы `ProfitFox.Tests.Unit` и `ProfitFox.Tests.Integration` по таблицам design.md, решение 10, вместе с
  папками эталонов; поправить строки путей к эталонам и `using static` на `Designs`. Проверить:
  - сборка — 0 предупреждений; тесты — 251 и 162;
  - сверка переноса — только строки путей к эталонам в `VerdictRulesTests`, `H0001ParityTests`, `H0001StrategyParityTests`,
    `OutcomeParityTests`;
  - десять файлов эталонов `Tests.Unit` — переименования на 100 %, `Tests.Integration/Fixtures` не тронута;
  - `BenchDatabase` в корне проекта, его `namespace` — `ProfitFox.Tests.Integration`; пустых каталогов после `git mv` нет.

## 8. Документы

- [x] 8.1 Обновить раздел «Слои» в `README.md`: раскладка «сначала вид, потом тема», тесты, файлы сборки, правило ссылок,
  граница расчёта по решению 9. Проверить: каждый путь, проект и тип, названный в разделе, существует; `Dockerfile`,
  `.claude/launch.json` и `ci.yml` указывают на существующие проекты и не менялись.

## 9. Итоговая проверка

- [x] 9.1 `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx` из `backend/`. Проверить: 0
  ошибок, 0 предупреждений; unit — 251, интеграционные — 162; проверка однострочных `<summary>` из `ci.yml` пуста.
- [x] 9.2 Результаты расчёта прежние: `EngineVersion.Number` равен 2, `EngineVersions.json` совпадает с `f5bc916` байт в
  байт (`cmp`), `EngineVersionTests` и `H0001ParityTests` зелёные.
- [x] 9.3 API прежнее: API на временной базе Postgres в Docker и порту 5291 (база разработки и порт 5240 не тронуты),
  `/health` — `200 Healthy`, `npm --prefix frontend run generate:api` с `PROFITFOX_OPENAPI_URL` на этот порт. Проверить:
  SHA-256 `schema.d.ts` равен базе; в таблице `VersionInfo` одна строка `202611200001`.
- [ ] 9.4 Образы: `docker build --file backend/src/ProfitFox.Presentation/Dockerfile .` и
  `docker build --file backend/src/ProfitFox.Worker/Dockerfile .` из корня собираются.
- [x] 9.5 Сверка переноса по всей ветке против `f5bc916`: 37 файлов, 61 строка — 49 объявлений `var`/явного типа (решение
  3), 8 полных имён (решение 11), 4 пути к эталонам (решение 10) — и два тега `<param>`; других расхождений нет. Проверить:
  `openspec validate align-structure-with-market-data --strict` проходит.

## Workflow follow-up

- Коммиты групп, push, пул-реквест, слияние и архив изменения — по слову владельца, после слияния `merge-bench-and-bot`.

## Открыто после применения

Итог против раздела «База»: unit — 251, интеграционные — 162, предупреждений сборки 0, SHA-256 `schema.d.ts` тот же,
`EngineVersions.json` тот же.

Ждёт владельца:

- **Коммиты не сделаны.** Правило владельца — без его слова не коммитить; задание другой сессии такого слова не даёт, и
  первый коммит был остановлен. Вся работа лежит в рабочем дереве ветки `claude/align-structure-with-market-data-2f82f2`
  (переносы проиндексированы как `git mv`). Каждая группа снята деревом git (`git write-tree`, без коммита и без ссылок):
  1 предложение `f5020c3`, 2 правки анализаторов `4b9ede9`, 3 файлы сборки `cb19252`, 4 Domain `c759584`, 5 Application
  `3f3cebd`, 6 Infrastructure `a304154`, 7 Presentation, Worker, Cli `9fa480d`, 8 тесты `c91b652`; последняя группа (README,
  генератор OpenAPI, эти артефакты) — рабочее дерево. По слову владельца из них собирается цепочка коммитов по группам
  (`git commit-tree`), каждая из которых проверена сборкой и тестами. Деревья без ссылок `git gc` удаляет не раньше чем через
  две недели.
- **Сборка образов (задача 9.4) упёрлась в сеть.** `docker build` образа API на состоянии со всеми переносами и файлами
  `Directory.*` собрался (`api exit 0`), но до цели `LeaveOutOpenApiXmlComments` в `ProfitFox.Presentation.csproj`;
  пересборка после неё зависла на `dotnet restore` (`api.nuget.org` с Мака отвечал медленно, `NU1900`). Образ Worker упал
  до сборки кода: `lookup mcr.microsoft.com: no such host` у Docker Desktop. Код и `Dockerfile` из-за этого не менялись.
  Остаётся повторить обе команды задачи 9.4, когда сеть оживёт.
- **Описания в OpenAPI (решение 1).** С `GenerateDocumentationFile` из образца пакет OpenAPI .NET 10 добавил бы в документ
  XML-комментарии (81 строка описаний в `schema.d.ts`). Генератор выключен в `ProfitFox.Presentation.csproj`, чтобы API
  осталось прежним. Включить описания — отдельное изменение вместе с `generate:api`.
- **`Tests.Unit` → Infrastructure (решение 2).** Расхождение с образцом: ссылка разрешена ради `InstrumentCatalogTests`, тест
  лежит в `Tests.Unit/Infrastructure/Clients/MarketData`. Перенести его в интеграционные тесты — отдельное решение.
- **Из образца не взято:** MediatR и обработчики, `Domain/Repositories`, `Entities`, `Events`, суффиксы `Internal`/`Db`/
  `Repository`, `scripts/verify-format.sh`, хук `pre-commit`, проверка формата в CI, `InternalsVisibleTo`.

Решения при применении, где задание и образец молчали:

- **Списки правила слоёв с транзитивными ссылками** (решение 2): Presentation и Worker получили Domain; без этого их сборка
  падала.
- **Проверка правила слоёв ссылкой Domain → Cli**, а не Domain → Application: вторая даёт цикл `MSB4006` раньше правила.
- **`.globalconfig` в `backend/`**, а не в корне: так в образце.
- **Порядок групп 2:** правки анализаторов до файлов сборки, чтобы каждая группа собиралась без предупреждений.
- **Раскладка видов, которых в образце нет** (решение 4): Domain `Services`, `Exceptions`, перечисления в
  `ValueObjects/<тема>/Enums`; Infrastructure `Clients/MarketData`; Presentation `Contracts/<тема>` с `Converters`,
  `HealthChecks`, `Formatters`; Worker `BackgroundServices`, `Logging`; Cli `Commands`, `Contracts`. Записи, которые отдают
  или берут порты, — в `Ports/<тема>` по правилу `conventions.md`.
- **Сортировка `using`:** `dotnet format` разделил группы пустой строкой (`dotnet_separate_import_directive_groups`
  образца) во всех файлах, где групп больше одной.
- **Файлы с правками `IDE0007`/`IDE0008`** (новые пути): `HypothesisCatalog`, `HypothesisRunner`, `SignalLibrary`,
  `PreviewHistoryCache`, `Refusals`, `GraphValidator`, `HypothesisReader`, `PairPosition`, `PairCandles`, `VerdictRules`,
  `CompiledGraph`, `InstrumentCatalog`, `SignalJson`, `SignalResponses`, `Program` (Presentation и Worker), тесты
  `BenchRepository`, `GrpcHistorySourceTests`, `MarketDataServer`, `FakeCandleFeed`, `FakeFundingFeed`, `WorkerTests`,
  `H0001ParityTests`, `H0001StrategyParityTests`, `StrategyAssemblyTests`, `PairPositionTests`, `PairStepsTests`,
  `TradeSimulatorTests`, `VerdictRulesTests`, `RunReportTests`, `Repository`. Теги `<param>` — `MarketHistory`.
