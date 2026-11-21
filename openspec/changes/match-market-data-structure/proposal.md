# Proposal

## Why

После `align-structure-with-market-data` папки слоёв названы как в `profit-fox-market-data`, но устройство кода осталось
прежним: в `ProfitFox.Domain` нет `Entities`, `Events` и `Repositories`, а сценарии Application — классы с несколькими
методами, а не команды и запросы с обработчиками. Владелец 2026-11-21 открыл Domain, не нашёл этих папок и сказал: «нам
нужна такая же структура как в market-data»; на вопрос о глубине выбрал «все слои одним изменением».

Тогдашнее решение «папки и файлы сборки, код не меняется» этим отменяется. Делать сейчас дешевле, чем потом: в Application
50 файлов и шесть сценариев, бот ещё не начат, параллельных веток в коде нет.

## What Changes

Поведение не меняется: те же маршруты API и документ OpenAPI, та же схема базы, те же результаты прогонов, та же команда
`bench`. Меняется устройство кода бэкенда — по `docs/conventions.md` образца (коммит `14a441d`).

- **Domain.** Появляются `Entities/Base/AggregateRoot` и `Events/IDomainEvent` (копия образца), `Entities/Runs/Run` и
  `Entities/Signals/SignalDocument` (сущности: закрытый конструктор, `Create`/`Restore`), `Repositories` с интерфейсами
  `IHypothesisRepository`, `ISignalRepository`, `IRunRepository`, `ILedgerRepository`. Интерфейсы приходят из
  `Application/Ports` вместе с записями, которые они отдают и берут, и с исключениями, которые бросает хранилище.
- **Application.** Подключается MediatR 12.5.0 (версия образца). Классы-сценарии `HypothesisCatalog`, `SignalLibrary`,
  `RunScheduler`, `RunBoard`, `HypothesisRunner`, `RunExecutor`, `SignalPreview` заменяются командами и запросами с
  обработчиками: команда — в папке `Commands/<Тема>/<Имя>/` со своим результатом, запрос и его обработчик — в
  `Queries/<Тема>/`. Контракты, которые показывает API, получают имена `<Имя>Internal` и конвертеры из типов домена. В
  `Ports` остаются только порты сервиса данных.
- **Infrastructure.** Хранилища становятся `<Имя>Repository` (`internal sealed`), их строки — `<Имя>Db`, SQL уходит в
  `Queries/<Имя>RepositoryQueries`, перевод строки в тип домена — в `Converters`. Регистрация зависимостей собирается в
  `InfrastructureRegistrar.AddInfrastructure` с `Mediatr/MediatrRegistrar`, `PostgresRegistrar` и регистрацией клиента
  сервиса данных; из двух `Program.cs` уходят одинаковые строки.
- **Presentation и Worker.** Контроллеры и служба Worker'а зовут Application только через `ISender`. Записи запросов и
  ответов API, маршруты и атрибуты не трогаются.
- **Тесты.** `Tests.Unit` перестаёт ссылаться на Infrastructure (`InstrumentCatalogTests` уходит в интеграционные), тесты
  сценариев раскладываются по обработчикам, в `Tests.Shared` появляются генераторы сущностей. Ни один тест не удаляется,
  ожидания и эталоны не правятся.
- **README**, раздел «Слои», приводится к новому устройству.

Что из образца не берётся и почему — в design.md, решение 8: транзакции в отдельной папке, обработчики запросов с SQL в
Infrastructure, общий обработчик исключений, перенос проверки базы в Infrastructure, NSubstitute, перевод строк статусов в
перечисления.

## Capabilities

### New Capabilities

Нет.

### Modified Capabilities

Нет: ни одно требование не меняется, поэтому изменение помечено `skip_specs: true`.

## Impact

- **Код бэкенда:** все шесть проектов `backend/src`, кроме `ProfitFox.Cli`, и все три проекта тестов. Application
  переписывается почти целиком (50 файлов → около 90: команда, обработчик и результат вместо метода класса); в остальных
  слоях — переносы, переименования и перевод вызовов на `ISender`. Расчёт (узлы, исполнение, сделки, вердикт) не правится.
- **Зависимости:** новый пакет `MediatR` 12.5.0 в Application; если `ILogger` не приходит с ним транзитивно —
  `Microsoft.Extensions.Logging.Abstractions` той же версии, что `Microsoft.Extensions.Hosting`. Других пакетов нет.
- **API, фронтенд, CLI:** не меняются; проверяется совпадением документа OpenAPI и `schema.d.ts` до и после.
- **База:** миграций нет; `EngineVersion.Number` и `Tests.Integration/Fixtures/EngineVersions.json` не меняются.
- **Журнал работы приложения:** строки хода прогона пишутся с именем обработчика вместо `HypothesisRunner` и
  `RunExecutor`; текст строк тот же.
- **Связь с другими изменениями:** делается от `release/0.1.0` (`882accb`). В `add-bench-service` открыты только шаги
  владельца по деплою, код они не трогают. Пока это изменение не слито, новые ветки от `release/0.1.0` с правками
  Application лучше не начинать: слияние с переписанным слоем будет ручным.
