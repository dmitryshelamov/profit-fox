# Design

## Context

Мотивация — в proposal.md. Состояние на `release/0.1.0` (`ed4d745`):

- `ProfitFox.Bench` ссылается на `ProfitFox.Engine.Strategy` и `ProfitFox.Engine.Simulation`, держит пакеты Grpc и
  Protobuf и `.proto` сервиса данных. Ввод-вывод в нём:
  - `HypothesisReader.Read` читает `hypothesis.json`;
  - `RunLedger` читает и дописывает `ledger.jsonl`;
  - `GitRepository` запускает `git`;
  - `InstrumentCatalog` и `GrpcHistorySource` ходят в сервис данных;
  - `SignalLibrary` пишет `bench/signals`;
  - `RunReport.Write` пишет отчёт, `ParityDump` — gzip-CSV.

  Всё это смешано с правилами в тех же классах.
- `ProfitFox.Bench.Presentation`:
  - `HypothesisList` читает папку `bench/hypotheses`;
  - `SignalPreview` — сценарий предпросмотра, который возвращает DTO API (`PreviewResponse`, `ErrorResponse`);
  - `PreviewHistoryCache` — кэш истории;
  - `Timeframes` повторяет таблицу таймфреймов `HypothesisReader`.
- `ProfitFox.Bench.Cli` собирает `HypothesisRunner` через `new`.
- Зона бота — заготовка: пять проектов с `Class1`, без ссылок друг на друга. Образцом служат имена слоёв, направление
  ссылок задаёт это изменение.
- Тесты бенча лежат в `ProfitFox.Tests.Unit/Bench/*` и `ProfitFox.Tests.Integration/Bench/*` и ссылаются на
  `ProfitFox.Bench`. Фейки: `FakeHistorySource : IHistorySource`, `FakeRepository : IRepository`, фейковый
  gRPC-сервер на сгенерированных `CandleFeed.CandleFeedBase` и `FundingPaymentFeed.FundingPaymentFeedBase`.
- Спецификации не называют проекты и классы, поэтому ни одно требование не меняется (`skip_specs: true`).

## Goals / Non-Goals

**Goals:**

- В Domain нет ввода-вывода, в Application нет адаптеров; это держат ссылки проектов, а не договорённость.
- В шаге 4 журнал, сигналы, гипотезы и отчёты переезжают в базу новым адаптером в Infrastructure. Сценарии и правила
  при этом не меняются.
- Тесты бенча проходят с теми же ожиданиями. Меняются только `using`, сборка объектов и в одном месте разбиение теста
  на правило и файл (решение 5).

**Non-Goals:**

- Новые возможности, исправления ошибок и смена текстов, кроме перечисленного в решениях 4 и 5. Где старый код
  делает что-то странно (например, `RepositoryRoot` в двух точках входа ищет корень по-разному), это остаётся как есть.
- Общий код с зоной бота: у бота кода нет.
- Тест архитектуры через рефлексию или NetArchTest: направление держат ссылки проектов (решение 1), а чистоту
  контроллеров проверяет поиск по коду в задачах.

## Decisions

### 1. Проекты и ссылки

```text
Engine.Strategy, Engine.Simulation
        ▲
Bench.Domain            без пакетов
        ▲
Bench.Application       без пакетов (System.Text.Json — из BCL)
        ▲
Bench.Infrastructure    Grpc.Net.Client, Google.Protobuf, Grpc.Tools, protos
        ▲
Bench.Presentation (Web)   Bench.Cli (Exe)   — оба ссылаются на Application и Infrastructure
```

- Domain ссылается только на движок. Application — на Domain. Infrastructure — на Application.
- Presentation и Cli — корни сборки. Они ссылаются на Application и Infrastructure. Infrastructure в них нужна только
  в `Program` и `BenchPresentation`, контроллеры видят только Application и Domain.
- `ProfitFox.Bench` удаляется.
- Альтернатива — Infrastructure, ссылающаяся только на Domain, и порты в Domain. Отклонена: владелец решил держать
  порты в Application. Domain остаётся набором правил, без интерфейсов.

### 2. Что куда переезжает

Пространства имён — `ProfitFox.Bench.<Слой>.<Папка>`, один тип на файл.

| Было (`ProfitFox.Bench.*`) | Станет |
|---|---|
| `Hypotheses`: `Hypothesis`, `Measure`, `TimeBlock`, `HypothesisException`, `HypothesisReader.Parse` и `Length` | `Domain.Hypotheses` |
| `HypothesisReader.Read` и `FileName`, нормализация `\r\n` | `Infrastructure.Hypotheses.HypothesisFiles` |
| `Presentation.Preview.Timeframes` и таблица таймфреймов `HypothesisReader` | `Domain.Hypotheses.Timeframes`, одна на оба места (решение 4) |
| `Runs`: `Blocks`, `Period`, `Periods`, `RandomEntries`, `SplitMix64`, `RunDesign`, `EventRow`, `RandomTrade`, `TradeFields`, `Evidence`, `PairEvaluation`, `MinutePaths` | `Domain.Runs` |
| `Verdicts/*` | `Domain.Verdicts` |
| `Ledger`: `LedgerEntry`, `RunStatus`, `LedgerException`; правила `CheckCanRun`, `Attempts`, `IsCorrected` | `Domain.Ledger`, правила в `LedgerRules` (решение 5) |
| `MarketData`: `Instrument`; `History`: `TimeRange`, `Coverage`, `HistoryRead`, `HistoryCandle`, `HistoryPayment`, `HistoryHash` | `Domain.MarketData` |
| `Signals`: `PairEvents`, `SignalNames`, `SignalDocument`, `NodePosition` | `Domain.Signals` |
| `Runs`: `HypothesisRunner`, `RunOptions`, `RunResult`, `PairData`, `UncommittedChangesException`; `RunReport.Text` | `Application.Runs` |
| `MarketData`: `IHistorySource`, `MarketHistory`, `HistoryUnavailableException`, `PeriodBoundaryException` | `Application.MarketData`, рядом новый порт `IInstrumentCatalog` |
| `Ledger.IRepository` | `Application.Runs.IRepository`, рядом новые порты `IRunLedger`, `IRunReports`, `IParityDumps`, `IParityDump` |
| `Signals`: `SignalLibrary` (правила), `SignalEntry`, `SignalProblem`, `SignalLibraryException` | `Application.Signals`, рядом порт `ISignalStore` |
| `Presentation.Hypotheses.HypothesisList` (чтение папки) | `Application.Hypotheses.HypothesisCatalog` над портом `IHypothesisStore` |
| `Presentation.Preview`: `SignalPreview`, `PreviewHistoryCache`, `PreviewHistory` | `Application.Preview`, с собственными типами результата (решение 7) |
| `MarketData.History.GrpcHistorySource`, `Intervals`, `MarketData/Protos/*.proto` | `Infrastructure.MarketData`, `csharp_namespace = "ProfitFox.Bench.Infrastructure.MarketData.Protos"` |
| `MarketData.InstrumentCatalog` (HTTP и `Parse`) | `Infrastructure.MarketData.InstrumentCatalog : IInstrumentCatalog` |
| `Ledger.RunLedger` (файл) | `Infrastructure.Ledger.LedgerFile : IRunLedger` |
| `Ledger.GitRepository` с `Changed` | `Infrastructure.Ledger.GitRepository : IRepository` |
| `RunReport.Write` (путь и запись) | `Infrastructure.Runs.ReportFiles : IRunReports` |
| `Runs.ParityDump` | `Infrastructure.Runs.ParityDump : IParityDump`, `ParityDumpFiles : IParityDumps` |
| `SignalLibrary` (файлы, временный файл, `PathOf`), `SignalJson` | `Infrastructure.Signals.SignalFiles : ISignalStore`, `SignalJson` остаётся `internal` |

Разбор текста гипотезы (`Parse`) — это правило формата и заморозки: хэш считается от текста. Поэтому он в Domain, а
чтение файла — в Infrastructure. Когда гипотезы окажутся в базе, текст и хэш останутся теми же. `HistoryCandle`
держит цены текстом, как их шлёт сервис. Это значение истории, и на нём держится точный хэш данных, поэтому оно тоже в
Domain.

### 3. Порты

Порты отвечают тем же, что и сейчас код, и бросают те же исключения.

```csharp
// Application.MarketData
interface IHistorySource { ReadCandles(...); ReadFunding(...); }                  // как сейчас
interface IInstrumentCatalog { Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken c); }
// HistoryUnavailableException — отказ обоих портов

// Application.Runs
interface IRunLedger { IReadOnlyList<LedgerEntry> Entries(); void Append(LedgerEntry entry); }
interface IRepository { IReadOnlyList<string> Uncommitted(); string Commit(); }  // как сейчас
interface IRunReports { string Write(RunDesign design, LedgerEntry entry, string text); } // где лежит отчёт
interface IParityDumps { IParityDump Open(string folder); }
interface IParityDump : IDisposable { void Write(PairData data, bool withEventOutcomes); void Finish(pseudo, blocks, entry); }

// Application.Hypotheses
interface IHypothesisStore {
    IReadOnlyList<string> Folders();   // папки гипотез, без порядка
    string Text(string folder);         // текст файла, \r\n → \n; HypothesisException «there is no hypothesis.json»
}

// Application.Signals
interface ISignalStore {
    IReadOnlyList<string> Names();                       // без скрытых и временных файлов, без порядка
    bool Exists(string name);
    SignalRead? Read(string name);                       // null — нет; иначе документ или ошибки разбора
    void Write(SignalDocument signal);                   // атомарно
    void Delete(string name);
}
```

- Порядок списков (`StringComparer.Ordinal`) и все проверки остаются в Application. Хранилище только хранит.
- Проверка имени до пути (`PathOf`) остаётся и в `SignalFiles`, как защита от `../`.
- У `IRunReports` и `IParityDump` сейчас по одному адаптеру, это семы на будущее. Без них Application писал бы файлы
  сам и нарушал решение 1. Отчёты в шаге 4 скорее всего уйдут в базу.
- Прогон получает зависимости явно, как сейчас:
  `HypothesisRunner(IHypothesisStore, IHistorySource, IInstrumentCatalog, IRepository, IRunLedger, IRunReports,
  IParityDumps, TextWriter log)`. Сейчас он получает папку стенда и сам строит пути `ledger.jsonl` и `reports/`. После
  изменения пути строят адаптеры (`LedgerFile(path)`, `ReportFiles(benchFolder)`), а `HypothesisRunner.LedgerPath`
  уходит. Обёртку из восьми портов в один объект не вводим: сборка в двух местах, и каждый порт там виден.

### 4. Одна таблица таймфреймов

`Timeframes` (1h, 4h, 1d) переезжает в `Domain.Hypotheses`. `HypothesisReader` берёт таймфрейм из неё вместо своего
словаря. Набор и текст ошибки «timeframe must be 1h, 4h or 1d» те же, предпросмотр и список гипотез используют ту же
таблицу. Это единственная склейка дублей в изменении.

### 5. Журнал: правила отдельно от файла

`LedgerRules.CheckCanRun(entries, hypothesis, hash, period, parity)` и `LedgerRules.Attempts(entries, hypothesis,
period)` — чистые функции над списком строк. Тексты исключений `LedgerException` прежние. Прогон читает
`ledger.Entries()` один раз на проверку. Сейчас `IsCorrected` перечитывает файл на каждую строку. Ответ тот же,
потому что файл во время проверки не меняется.

`RunLedgerTests` делится. Проверка «строки пишутся и читаются» идёт на `LedgerFile`, проверки правил — на `LedgerRules`
над строками, записанными через `LedgerFile`. Ожидания, имена тестов и тексты в `Contains` не меняются.

Альтернатива — оставить класс `RunLedger(IRunLedger)` в Application, чтобы тесты почти не трогать. Отклонена: это
тонкий проход без своего поведения, и тест «удалить модуль» он не проходит.

### 6. Библиотека сигналов

`Application.Signals.SignalLibrary(ISignalStore)` сохраняет все операции и их порядок проверок:

- `List` и `Read`;
- `Create`: сначала имя и граф, потом занятость;
- `Replace`: сначала существование, потом граф;
- `Rename`: запись нового и удаление старого, как сейчас, без атомарности;
- `Delete`.

Проблемы (`Invalid`, `NotFound`, `NameTaken`, `Unreadable`) и тексты прежние. `SignalFiles` пишет через временный
файл `.<имя>.<guid>.tmp` и не видит в `Names` файлы, начинающиеся с точки.

В `SignalLibraryTests` меняется только сборка: `new SignalLibrary(new SignalFiles(_folder))`. Тест по-прежнему читает
файлы папки напрямую.

### 7. Предпросмотр и список гипотез без DTO в Application

`Application.Preview.SignalPreview.Run(PreviewQuery, CancellationToken)`. `PreviewQuery(string? Instrument, string?
Timeframe, JsonElement? Signal)` — поля запроса как есть. Ответ — `PreviewOutcome`, в нём одно из двух:

- `SignalPreviewResult` с полями `Instrument`, `TimeSpan Timeframe`, свечи с торгами (`HistoryCandle`), покрытие
  (`TimeRange`), моменты событий и конец разведки;
- список `PreviewError(Code, Reason, Field, GraphError? Graph)`.

Порядок проверок и ошибок прежний: таймфрейм, граф, пара. Каталог пар читается, только если пара задана. История
читается, только когда ошибок нет. Presentation переводит ответ в `PreviewResponse` и `ErrorResponse` прежним кодом:
миллисекунды, `double` из текста, `RequestErrors.Of` для ошибок графа. Поэтому JSON ответа и OpenAPI-схема не меняются.

`Application.Hypotheses.HypothesisCatalog` над `IHypothesisStore`:

- `Read(folder)` — `HypothesisReader.Parse(folder, store.Text(folder))`. Его же использует прогон.
- `List()` — элементы `(Id, Name, TimeSpan? Timeframe, JsonElement? Signal, string? Error)` в порядке папок.
  Запасной id и имя при ошибке берутся из имени папки, как сейчас.

Сейчас `HypothesisList` читает файл второй раз без нормализации `\r\n`. Теперь сигнал берётся из того же текста. Перевод
строки в JSON — только пробел между лексемами, поэтому `signal` в ответе тот же.

### 8. Сборка зависимостей

- `BenchPresentation.AddBenchPresentation(BenchFolder)` регистрирует:
  - файловые адаптеры: `HypothesisFiles` и `SignalFiles` на `bench/hypotheses` и `bench/signals`;
  - сценарии Application: `HypothesisCatalog`, `SignalLibrary`, `PreviewHistoryCache`, `SignalPreview`;
  - контроллеры, JSON и OpenAPI, как сейчас.
- `Program` регистрирует gRPC и REST: `IHistorySource` и `IInstrumentCatalog`. Сейчас регистрируется конкретный
  `InstrumentCatalog`, теперь — по порту.
- Тестовый `BenchPresentationServer` регистрирует те же адаптеры на фейковом сервере по портам. Это его единственная
  правка.
- Пакет `Microsoft.Extensions.DependencyInjection.Abstractions` в Application и Infrastructure не нужен: регистрация
  живёт в корне сборки.
- `Cli/Program` собирает `HypothesisRunner` через `new`:
  - `LedgerFile(<root>/bench/ledger.jsonl)`;
  - `ReportFiles(<root>/bench)`;
  - `ParityDumpFiles`;
  - `HypothesisFiles`.

  Список исключений с кодом выхода 1 прежний, меняются только пространства имён.

### 9. Тесты на месте

Папки тестов (`Tests.Unit/Bench/Runs`, `.../Signals` и т. д.) не переезжают по слоям. Меняются ссылки проектов,
`using` и сборка объектов. Проверка в задачах: в `git diff` по `backend/tests` ожидания не меняются. В строках с
`Assert`, `Is.`, `Has.`, `Does.`, `Throws.` меняются только имена того, что проверяется, а ожидаемые значения и тексты
остаются:

- `ledger.CheckCanRun(…)` и `ledger.Attempts(…)` становятся `LedgerRules.…(ledger.Entries(), …)` (решение 5);
- `new RunLedger(…)` становится `new LedgerFile(…)`;
- пространство имён protos в `Has.All.Matches<…>` в `PreviewTests` меняется на новое.

### 10. Сверка H0001

Владелец решил 2026-11-19: отдельной сверки H0001 на реальных или фейковых данных в этом изменении нет. Её
заменяют существующие тесты, которые проходят без правок ожиданий:

- `H0001ParityTests` движка — не затронут;
- `HypothesisRunTests` — прогон и сверочный прогон H0001 на фейковом сервисе, с выгрузкой;
- `RunReportTests`, `RunRulesTests`, `VerdictRulesTests`.

## Risks / Trade-offs

- [Пространство имён protos меняется, а `.proto` по правилу копируются из сервиса данных без правок, кроме
  `csharp_namespace`] → меняется только эта строка, сообщения не трогаем. Фейковый gRPC-сервер тестов переходит на
  новое пространство имён.
- [Типы перехода, которые потеряли общий проект: `PairData` держит `HistoryRead` из Domain и попадает в порт
  `IParityDump`] → это значения Domain, Infrastructure их видит через Application. Цикла нет.
- [Восемь параметров у `HypothesisRunner`] → осознанно (решение 3). Если в шаге 4 их станет больше, вопрос
  поднимается там.
- [В шаге 4 «проверить и дописать» журнала в базе должно идти одной транзакцией, а порт `Entries` + `Append` её не
  даёт] → сейчас файл и один процесс, гонки нет. Форма порта под базу решается в шаге 4, правила `LedgerRules` от неё
  не зависят.
- [Молчаливая смена поведения при переносе, особенно порядка проверок предпросмотра и библиотеки сигналов] → тесты
  `PreviewTests`, `SignalsTests`, `SignalLibraryTests` проходят без правок ожиданий. OpenAPI-схема сравнивается до и
  после.
- [Сборка решения с `-warnaserror` падает на NU1903 в `ProfitFox.Bot.Presentation` и до изменения] → проекты стенда и
  тестов собираются по одному с `-warnaserror`.

## Migration Plan

Развёртывать нечего: стенд запускается на Маке владельца из репозитория. Откат — отмена PR. Файлы `bench/`
(гипотезы, сигналы, журнал, отчёты) не меняются и читаются новым кодом как есть.
