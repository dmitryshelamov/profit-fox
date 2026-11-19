# Tasks

Каждая группа оставляет стенд собранным и все тесты зелёными. «Сборка» ниже значит
`dotnet build <проект> -warnaserror` для каждого проекта `backend/src/bench/*` и обоих тестовых проектов. Решение целиком
с `-warnaserror` падает на NU1903 в `ProfitFox.Bot.Presentation` и до изменения. «Тесты» значит
`dotnet test backend/tests/ProfitFox.Tests.Unit` и `dotnet test backend/tests/ProfitFox.Tests.Integration`. Все тесты
проходят, их столько же, сколько в замере 1.1, в `git diff -- backend/tests` нет изменённых строк проверок (design.md,
решение 9).

## 1. Замер до изменения

- [x] 1.1 На `ed4d745`, до правок, записать в `scratchpad` (вне репозитория):
  - число пройденных тестов Unit и Integration;
  - `openapi/v1.json` запущенного API (`dotnet run --project backend/src/bench/ProfitFox.Bench.Presentation`,
    `curl localhost:5240/openapi/v1.json`);
  - `schema.d.ts` из `PROFITFOX_OPENAPI_URL=... openapi-typescript ... -o <scratchpad>/schema.before.d.ts`.

  Проверка: три файла есть, `schema.before.d.ts` совпадает с `frontend/src/api/generated/schema.d.ts`.

## 2. Domain

- [x] 2.1 Создать `ProfitFox.Bench.Domain` (classlib, net10.0, ссылки только на `ProfitFox.Engine.Strategy` и
  `ProfitFox.Engine.Simulation`) и добавить в `backend/ProfitFox.slnx`. Через `git mv` перенести типы строк «Domain»
  таблицы design.md, решение 2:
  - `Hypotheses` с `HypothesisReader.Parse` и `Length`;
  - `Runs`, `Verdicts`;
  - `Ledger` без файла;
  - `MarketData` — значения истории и `Instrument`;
  - `Signals`: `PairEvents`, `SignalNames`, `SignalDocument`, `NodePosition`.

  Пространства имён — по папкам. `ProfitFox.Bench` и тестовые проекты ссылаются на Domain. Проверка: сборка, тесты,
  `grep -rn "System.IO\|File\.\|Directory\.\|Process" backend/src/bench/ProfitFox.Bench.Domain` пуст.
- [x] 2.2 `HypothesisReader.Read` и `FileName` остаются в `ProfitFox.Bench` до группы 4. В Domain — только `Parse(folder,
  text)`. Проверка: `HypothesisReaderTests` проходят без правок ожиданий.
- [x] 2.3 `Timeframes` в `Domain.Hypotheses`; `HypothesisReader` и предпросмотр берут таймфреймы из неё (design.md,
  решение 4). Проверка: тест `HypothesisReaderTests` на неверный таймфрейм и `PreviewTests` на `InvalidTimeframe`
  проходят без правок.
- [x] 2.4 `LedgerRules.CheckCanRun` и `Attempts` над списком строк, тексты `LedgerException` прежние (решение 5).
  `RunLedger` пока остаётся файлом и зовёт эти правила. `RunLedgerTests` перенести на `LedgerRules` над строками
  файла. Проверка: тесты; `git diff` теста показывает только перенос и сборку, строки `Assert` те же.

## 3. Application

- [x] 3.1 Создать `ProfitFox.Bench.Application` (ссылка только на Domain), добавить в решение. Порты по design.md,
  решение 3: `IHistorySource`, `IInstrumentCatalog`, `IRunLedger`, `IRepository`, `IRunReports`, `IParityDumps`,
  `IParityDump`, `IHypothesisStore`, `ISignalStore`. Рядом `HistoryUnavailableException`, `PeriodBoundaryException`,
  `MarketHistory`. `ProfitFox.Bench` ссылается на Application, и его классы реализуют порты:
  - `GrpcHistorySource`, `InstrumentCatalog`, `GitRepository`;
  - `RunLedger` → `LedgerFile`;
  - `ParityDump`.

  Проверка: сборка, тесты (`GrpcHistorySourceTests`, `InstrumentCatalogTests`, `MarketHistoryTests`,
  `CoverageTests`).
- [x] 3.2 `HypothesisRunner`, `RunOptions`, `RunResult`, `PairData`, `UncommittedChangesException` и `RunReport.Text` —
  в `Application.Runs`, на портах (решение 3). Запись отчёта переходит в `ReportFiles : IRunReports`, имя файла
  прежнее. Выгрузку открывает `IParityDumps`. В `HypothesisRunTests.Runner` и в `Cli/Program` поменять сборку.
  Проверка: `HypothesisRunTests` и `RunReportTests` проходят без правок ожиданий; `bench run` с неверной командной
  строкой выходит с кодом 2, как раньше.
- [x] 3.3 `SignalLibrary` в `Application.Signals` над `ISignalStore`; `SignalFiles` с `SignalJson` и записью через
  временный файл (решение 6). `SignalEntry`, `SignalProblem`, `SignalLibraryException` — в Application. Проверка:
  `SignalLibraryTests` (меняется только `new SignalLibrary(new SignalFiles(_folder))`) и `SignalsTests` проходят без
  правок ожиданий.
- [x] 3.4 `HypothesisCatalog` (`Read`, `List`) над `IHypothesisStore` в `Application.Hypotheses` (решение 7). Прогон
  читает гипотезу через него. `HypothesisFiles : IHypothesisStore` — с `FileName`, текстом «there is no
  hypothesis.json» и нормализацией `\r\n`. Удалить `HypothesisReader.Read` и `Presentation.Hypotheses.HypothesisList`.
  `HypothesesController` берёт `HypothesisCatalog` и переводит элементы в `HypothesisResponse`. В тестах:
  - `HypothesisReader.Read(folder)` заменяется на `HypothesisCatalog` над `HypothesisFiles`, в том числе в тесте хэша
    файла с `\r\n`;
  - `HypothesisReader.FileName` заменяется на `HypothesisFiles.FileName`.

  Проверка: `HypothesisRunTests`, `HypothesisReaderTests`, `PreviewTests` на `GET /api/hypotheses` проходят без правок
  ожиданий.
- [x] 3.5 `SignalPreview`, `PreviewHistoryCache`, `PreviewHistory` — в `Application.Preview`, с `PreviewQuery`,
  `PreviewOutcome`, `SignalPreviewResult`, `PreviewError` (решение 7). `PreviewController` переводит ответ в
  `PreviewResponse` и `ErrorResponse`. Порядок ошибок прежний: таймфрейм, граф, пара. `Presentation.PreviewResult` уходит.
  Проверка: `PreviewTests` и `PreviewCacheTests` проходят без правок ожиданий; в Application нет `using
  ProfitFox.Bench.Presentation` и `Microsoft.AspNetCore`.

## 4. Infrastructure

- [x] 4.1 Переименовать `ProfitFox.Bench` в `ProfitFox.Bench.Infrastructure` через `git mv`: папка, `.csproj`,
  `ProfitFox.slnx`, ссылки Cli, Presentation и тестов. Раскладка по папкам `MarketData`, `Hypotheses`, `Signals`,
  `Ledger`, `Runs`, пространства имён — по папкам. В обоих `.proto` поменять только `csharp_namespace` на
  `ProfitFox.Bench.Infrastructure.MarketData.Protos`. Фейковый gRPC-сервер тестов перевести на новое пространство
  имён. Проверка: сборка, тесты; `git diff` по `.proto` — по одной строке на файл; в `backend/src` не осталось
  `ProfitFox.Bench\b` вне `ProfitFox.Bench.<Слой>`.
- [x] 4.2 Сборка зависимостей по решению 8:
  - `AddBenchPresentation(BenchFolder)` — файловые адаптеры и сценарии;
  - `Program` — `IHistorySource` и `IInstrumentCatalog`;
  - `BenchPresentationServer` тестов — те же адаптеры на фейковом сервере;
  - `InstrumentsController` — через `IInstrumentCatalog`.

  Проверка:
  - интеграционные тесты API проходят;
  - `grep -rn "ProfitFox.Bench.Infrastructure" backend/src/bench/ProfitFox.Bench.Presentation/Controllers` пуст;
  - `grep -rn "Infrastructure\|Grpc\|System.Diagnostics.Process" backend/src/bench/ProfitFox.Bench.Application` пуст.

## 5. Проверка целиком

- [x] 5.1 Сборка всех проектов стенда и тестов с `-warnaserror` и тесты. Пройденных тестов столько же, сколько в 1.1.
  В `git diff ed4d745 -- backend/tests` ожидания не меняются, в строках проверок меняются только имена (решение 9).
- [x] 5.2 Запустить API на новом коде. Снять `openapi/v1.json` и `schema.after.d.ts` так же, как в 1.1. Проверка:
  `diff` с замером 1.1 пуст, `npm --prefix frontend run generate:api` не меняет
  `frontend/src/api/generated/schema.d.ts` (`git status` чистый).
- [x] 5.3 В README в раздел о стенде — абзац «Слои стенда»: четыре проекта, направление ссылок и где искать порт для
  нового хранилища. Проверка: пути и имена проектов в абзаце существуют.

## Workflow follow-up

- Коммит, push и PR в `release/0.1.0` — только после «да» владельца на каждый шаг.
- После слияния архивировать `split-bench-layers` отдельным PR.
- Следующий шаг плана — 4, проверка на стенде из конструктора: журнал и сигналы в базе новым адаптером Infrastructure.
