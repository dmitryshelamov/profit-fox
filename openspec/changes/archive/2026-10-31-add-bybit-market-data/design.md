# Design

## Context

Мотивация — см. `proposal.md`, требования — `specs/market-data-import` и `specs/market-data-query`.

Текущее состояние: решение `backend/ProfitFox.sln` содержит только скаффолд — слои `Domain` → `Application` →
`Infrastructure` → `Presentation` (ASP.NET Core Web API с контроллерами) и проекты `Tests.Unit` / `Tests.Integration` (
NUnit). Внешних зависимостей, БД и предметного кода нет. Термины — в `CONTEXT.md`.

Ограничения проекта, влияющие на решение: деньги, цены и объёмы — только `decimal`; время — только UTC `DateTimeOffset`,
без `DateTime.Now`; тесты и локальные запуски не отправляют реальных ордеров; новые внешние зависимости вводятся только
через этот дизайн; секреты — только через переменные окружения / user secrets.

## Goals / Non-Goals

**Goals:**

- Ввести первое хранилище и первый внешний интеграционный адаптер так, чтобы последующие фичи (бэктест,
  автоактуализация, live) переиспользовали их без переделки.
- Сделать правила сохранения (закрытые свечи, границы периода, идемпотентность) проверяемыми unit-тестами без сети и БД.

**Non-Goals:**

- Параллельное выполнение нескольких задач загрузки и горизонтальное масштабирование сервиса.
- Отмена задачи пользователем и автоматическое возобновление прерванных задач.
- Пропуск уже загруженных диапазонов при повторной загрузке (см. решение 6).
- Обнаружение и отчёт о «дырах» в данных Bybit.

## Decisions

### 1. Источник: публичный REST API Bybit v5, mainnet, без ключа

Свечи — `GET /v5/market/kline` (`category`, `symbol`, `interval`, `start`, `end` в миллисекундах, `limit` до 1000;
`result.list` отсортирован по убыванию `startTime`, элементы — строки
`[startTime, open, high, low, close, volume, turnover]`). Проверка символа —
`GET /v5/market/instruments-info?category=&symbol=`. Базовый адрес задаёт окружение SDK (`BybitEnvironment.Live`).

- Рыночные данные публичные: API-ключ не нужен, ордера не отправляются, поэтому mainnet безопасен и правило «никаких
  реальных ордеров» не затрагивается.
- Testnet отклонён: его история неполная и не совпадает с реальной.
- Уточнено при ревью: доступ к Bybit — через SDK `Bybit.Net` из мета-пакета `CryptoClients.Net` 5.9.0 (`Bybit.Net`
  7.7.0, `CryptoExchange.Net` 13.1.0), как в прежнем проекте; адаптер вызывает `IBybitRestClient.V5Api.ExchangeData` (
  `GetKlinesAsync`, `GetSpotSymbolsAsync`, `GetLinearInverseSymbolsAsync`), ответы в `decimal` разбирает SDK. Исходное
  решение «без SDK» отменено владельцем. `CryptoClients.Net` 5.10.0 не взят: ему меньше двух недель. `Bybit.Net` сам
  настраивает обработчик своего `HttpClient`, поэтому тесты регистрируют `IBybitRestClient` поверх заглушки HTTP
  целиком — ни один тест не доходит до настоящего Bybit; ключ `Bybit:BaseAddress` удалён. Встроенный ограничитель
  частоты SDK включён; в unit-тестах выключен, чтобы паузы не шли по реальным часам.

### 2. Постраничная выгрузка от конца периода к началу

Адаптер запрашивает страницы по 1000 свечей, двигая `end` назад (`end` = open time самой ранней полученной свечи − 1
мс), пока не дойдёт до `from` или Bybit не вернёт пустую страницу. Каждая страница сразу сохраняется — так прогресс
задачи растёт, а при ошибке сохранённое остаётся (требование «Ошибка биржи»). Так работает пагинация Bybit (ответ по
убыванию), отдельного запроса «найти начало истории» не нужно; это же покрывает «Период до листинга».

### 3. Правило закрытой свечи — чистая доменная функция с инжектируемым временем

Перечисление `Interval` в `Domain` (`OneMinute` … `TwelveHours`, `OneDay`, `OneWeek`, `OneMonth`) знает конец свечи по
её open time (`CandleEnd`): фиксированная длительность для минутных интервалов и суток, 7 суток для недели, начало
следующего календарного месяца UTC для месяца. Свеча закрыта, если её конец ≤ «сейчас». «Сейчас» берётся из
`TimeProvider` (.NET), в тестах — `FakeTimeProvider`. Конец периода по умолчанию (`to` не указан) — тоже из
`TimeProvider` в момент создания задачи. Фильтрация закрытых свечей и границ `[from, to)` делается в `Application` до
сохранения, поэтому проверяется unit-тестами на эталонных наборах.

### 4. Хранилище: PostgreSQL + TimescaleDB, схема через FluentMigrator, доступ через Dapper (Npgsql)

- Таблица `candles`: `category`, `symbol`, `interval`, `open_time timestamptz`, `open`, `high`, `low`, `close`,
  `volume`, `turnover` — `numeric` без фиксированной точности (значение хранится ровно как пришло). Первичный ключ
  `(category, symbol, interval, open_time)`. Hypertable по `open_time`.
- Таблица `candle_import_jobs`: идентификатор (`uuid`), параметры, статус, число сохранённых свечей, причина ошибки,
  `created_at` / `started_at` / `finished_at`.
- Схема — миграции FluentMigrator (`FluentMigrator.Runner.Postgres`) в
  `Infrastructure/Persistence/Postgres/FluentMigrator/Migrations`; каждая миграция — raw SQL через `Execute.Sql` (DDL
  таблиц, `CREATE EXTENSION timescaledb`, `create_hypertable`), без fluent-построителя таблиц. Миграции применяются при
  старте сервиса (на этом этапе один экземпляр, без отдельного шага деплоя).
- Уточнено при ревью: миграции пишутся на raw SQL — DDL читается как есть, без перевода с fluent-API. После
  переписывания схема сверена с прежней по `information_schema`, `pg_constraint`, `pg_indexes` и
  `timescaledb_information` и совпадает полностью.
- Доступ к данным — Dapper поверх `NpgsqlDataSource`, SQL пишется вручную. Upsert свечей — один SQL-запрос на страницу:
  `INSERT ... SELECT FROM unnest(...) ON CONFLICT (pk) DO UPDATE`; массивы передаются параметрами Npgsql напрямую,
  потому что Dapper разворачивает списки в `IN (...)`.
- Уточнено при ревью: EF Core заменён — владелец выбрал FluentMigrator + Dapper ради явного SQL и миграций без снимков
  модели. EF Core, `DbContext`, его миграции и локальный инструмент `dotnet-ef` удалены. Версии: FluentMigrator 8.0.1,
  Dapper 2.1.89, Npgsql 10.0.3. Из FluentMigrator подключён только `FluentMigrator.Runner.Postgres` (он тянет
  `Runner.Core`): мета-пакет `FluentMigrator.Runner` приносит раннеры всех СУБД, в том числе SQL Server с
  `Microsoft.Data.SqlClient` и уязвимым `System.Security.Cryptography.Pkcs` 9.0.0 (NU1902). Dev-БД, созданную миграциями
  EF Core, нужно пересоздать (`docker compose down -v`): данных для переноса нет.
- Альтернативы: EF Core (исходный выбор) — миграции и маппинг из модели, но change tracking бесполезен для пакетов по
  1000 строк, upsert всё равно raw SQL, а снимки модели и design-time фабрика усложняют проект; чистый Npgsql без
  Dapper — меньше зависимостей, но больше ручного маппинга строк; чистый PostgreSQL без TimescaleDB — выбран владельцем
  вариант с TimescaleDB ради сжатия и быстрых диапазонных запросов на больших объёмах минутных свечей.

### 5. Фоновое выполнение: in-process очередь и один воркер

`POST` сохраняет задачу (`Pending`) и кладёт её идентификатор в `Channel<Guid>`; `BackgroundService` берёт задачи по
одной и выполняет последовательно. Состояние задачи живёт в БД, очередь — только сигнал. При старте, до приёма запросов,
все задачи `Pending`/`Running` переводятся в `Failed` («прервана перезапуском сервиса»).

- Последовательное выполнение держит нагрузку далеко под лимитом Bybit (600 запросов / 5 с на IP) без отдельного rate
  limiter.
- Альтернативы: Hangfire/Quartz — избыточная зависимость ради одной очереди; возобновление прерванных задач — лишнее
  поведение, раз повторный запуск идемпотентен и дешёв.
- Уточнено при ревью: сбой одной задачи вне обработки ошибок обработчика (например, БД недоступна при сохранении
  статуса) не останавливает фоновую службу — по умолчанию необработанное исключение в `BackgroundService` останавливает
  весь хост. Служба логирует ошибку и берёт следующую задачу; задача остаётся в последнем сохранённом статусе и
  переводится в `Failed` при следующем старте.

### 6. Идемпотентность через upsert, без пропуска загруженных диапазонов

Повторная задача всегда заново выгружает весь запрошенный период и делает upsert. Результат совпадает с «дозагрузкой
только недостающего», а логики вычисления пропусков нет. Цена — лишние запросы к Bybit при повторной загрузке длинного
периода (год минутных свечей ≈ 526 запросов, единицы минут). Оптимизация — отдельной фичей, если станет заметной.

### 7. Повторы при ошибках Bybit

Повтор с экспоненциальной паузой (1, 2, 4, 8, 16 с; не более 5 повторов) на HTTP 429, 5xx, таймаут и `retCode` =
`10006` (rate limit). Остальные ненулевые `retCode` — сразу `Failed` с текстом `retMsg`. Пауза — через `TimeProvider`,
поэтому тесты повторов не ждут реального времени.

- Уточнено при ревью: повтор и circuit breaker — конвейер Polly 8.8.0 (`ResiliencePipeline`) по образцу
  `ExchangeResilienceExecutor` прежнего проекта, паузы — через `TimeProvider`. Повторяемость определяется по результату
  SDK: HTTP 429 и 5xx, таймаут, нет соединения, `retCode` 10006; остальные ошибки — сразу `ExchangeException`. Circuit
  breaker общий для всех запросов к Bybit, стоит внутри повтора и считает каждую попытку: при доле ошибок не меньше 50 %
  и хотя бы 10 попытках за 60 с цепь размыкается на 15 с, и запросы сразу завершаются `ExchangeException`. Одна операция
  делает до 6 попыток и сама цепь не размыкает. Значения прежнего проекта (4 запроса за 30 с) не взяты: с пятью
  повторами этого решения цепь размыкалась бы раньше, чем они закончатся. Проверка символа (решение 8) идёт через тот же
  circuit breaker, без повторов. Параметры — константы.

### 8. Проверка символа синхронно в `POST`

Символ проверяется до создания задачи через `instruments-info`: пустой `result.list` → 400 по полю `symbol`; Bybit
недоступен → 503. Пользователь сразу видит ошибку в символе, а не узнаёт о ней из упавшей задачи.

- Уточнено при реализации: проверка — одна попытка без повторов из решения 7. Пользователь ждёт ответа `POST` синхронно,
  а полный цикл повторов занял бы 31 с. Недоступностью считается любой сбой: ошибка соединения, таймаут, не-2xx ответ
  или ненулевой `retCode`.

### 9. HTTP API

- `POST /api/market-data/candle-imports` — тело `{ category, symbol, interval, from, to? }` → `202 Accepted`, `Location`
  на задачу, тело — задача.
- Уточнено при ревью: команда `CreateCandleImport` возвращает только идентификатор — команды CQRS не возвращают данных;
  тело ответа `POST` контроллер читает запросом `GetCandleImport`. Фоновая служба может успеть взять задачу, поэтому в
  теле бывает не только `Pending`, но и `Running` или итоговый статус. Интеграционный тест «Корректный запрос» отключает
  выполнение задач (очередь подменена моком), чтобы проверить `Pending` без гонки.
- `GET /api/market-data/candle-imports/{id}` → `200` с задачей или `404`.
- `GET /api/market-data/candles?category=&symbol=&interval=&from=&to=&limit=` → `200` со списком свечей; `from` и `to`
  обязательны.
- Ошибки валидации — `400` в формате `ProblemDetails` (`errors` по полям); недоступность Bybit — `503`.
- `category` — строки `spot` / `linear`; `interval` — коды Bybit (`1` … `720`, `D`, `W`, `M`), как в `CONTEXT.md`;
  символ приводится к верхнему регистру. Время — ISO 8601 со смещением или `Z`; смещение приводится к UTC; время без
  смещения отклоняется (`400` по полю), чтобы результат не зависел от часового пояса сервера.
- Уточнено при ревью: `from` и `to` приходят строками и разбираются в обработчиках (`ApiTimestamps`), как категория и
  интервал: и System.Text.Json, и привязка запроса MVC читают время без смещения как местное. Обязательные поля
  контрактов — `[Required]`, а не `required` C#: отсутствующее или пустое поле даёт `400` по имени параметра, а не по
  `$`.
- `decimal` в JSON — строками (`"0.000123"`): JSON-числа в JavaScript-клиентах теряют точность. Уточнено при реализации:
  вместо конвертера System.Text.Json DTO свечи сразу содержит строки (`CandleResponse`). Эффект тот же — только для
  свечей, — но без конвертера с неиспользуемым чтением.
- Уточнено при реализации: ошибка «пустой период» и «`from` в будущем» возвращается по полю `from`. Ключи ошибок —
  JSON-имена параметров (`SystemTextJsonValidationMetadataProvider`). Причина прерванной задачи —
  `Interrupted by service restart.`; все тексты ошибок API на английском, как и `retMsg` Bybit.

### 10. Раскладка по слоям

- `Domain`: сущности `Candle` и `CandleImportJob` — в `Entities/MarketData`; перечисления `MarketCategory`, `Interval` (
  с доменным `CandleEnd`) и `CandleImportStatus` — в `Entities/MarketData/Enums`; value objects `Instrument` (
  категория + символ), `Price`, `Volume` — в `ValueObjects/MarketData`. Namespace совпадают с папками; тесты в
  `Tests.Unit/Domain` повторяют ту же раскладку.
- Уточнено при ревью: домен не знает строковых кодов. Коды API (`spot`/`linear`, `1` … `720`, `D`, `W`, `M`) разбирает и
  формирует `Application/MarketData/MarketDataCodes`; ими же пишутся колонки `category` и `interval` в БД, поэтому схема
  и данные не меняются. Адаптер Bybit переводит значения в перечисления SDK (`Category`, `KlineInterval`) сам (
  `Infrastructure/Exchanges/Bybit/BybitCodes`, `internal`, unit-тесты видят его через `InternalsVisibleTo`): это внешний
  контракт, и он может разойтись с кодами API. Так же сделано в прежнем проекте: `MarketDataTimeframe` — enum, строки —
  в конвертерах.
- Уточнено при ревью: цены свечи (`Open`, `High`, `Low`, `Close`) — `Price`, объёмы (`Volume`, `Turnover`) — `Volume`.
  Оба — обёртки над `decimal` без инвариантов: свеча хранит значения ровно так, как их вернула биржа (требование «Состав
  сохраняемой свечи»). Адаптеры — парсер Bybit, хранилище, DTO API — переводят их в `decimal` и обратно; схема БД и
  формат API не меняются.
- Уточнено при ревью: базовый класс агрегатов `AggregateRoot` (список доменных событий: добавить, прочитать, очистить) —
  в `Entities/Base`, маркерный интерфейс `IDomainEvent` — в `Events`. `Candle` от него не наследуется: событий у свечи
  нет, она остаётся record с равенством по значению. База вводится заранее, для будущих агрегатов (стратегии, ордера), —
  сознательное исключение из правила «ничего на потом».
- Уточнено при ревью: порты-хранилища `ICandleRepository` и `ICandleImportJobRepository` — в `Domain/Repositories` (
  namespace `ProfitFox.Domain.Repositories`): их сигнатуры используют только доменные типы. Задача загрузки — доменное
  понятие (`CONTEXT.md`, жизненный цикл из спека `market-data-import`) с идентичностью, поэтому сущность; она остаётся
  record без `AggregateRoot`: событий нет, статусы меняет обработчик `RunCandleImport`. `IMarketHistoryClient` и
  `ICandleImportQueue` остаются в `Application`: это порты к внешнему источнику и к механизму фонового выполнения, а не
  хранилища.
- `Application`: CQRS на MediatR. Команды — `CreateCandleImport` («создать задачу»), `RunCandleImport` («выполнить
  задачу»), `FailInterruptedImports` («пометить прерванные»); запросы — `GetCandles` («прочитать свечи»),
  `GetCandleImport` (задача по идентификатору). Каждый сценарий — `<Имя>Command` или `<Имя>Query` с обработчиком в
  `MarketData/Commands/<Имя>/` или `MarketData/Queries/<Имя>/`. Порты `IMarketHistoryClient` и `ICandleImportQueue` — в
  `Ports/MarketData` (namespace `ProfitFox.Application.Ports.MarketData`), как в прежнем проекте.
- Уточнено при ревью: ядро не привязано к Bybit — в будущем будут другие биржи. Порт `IMarketHistoryClient` (история
  свечей постранично и проверка символа) и исключение `ExchangeException` не упоминают биржу; Bybit знает только адаптер
  `Infrastructure/Exchanges/Bybit`, он реализует порт и бросает `ExchangeException`. Измерение «биржа» (в API, БД и
  `Instrument`) и выбор клиента по бирже, как `IMarketHistoryClient.ExchangeType` в прежнем проекте, не вводятся: другие
  биржи вне рамок изменения, их добавит своё изменение.
- Уточнено при ревью: валидация параметров остаётся в обработчике, без pipeline behaviors и FluentValidation. Команды не
  возвращают данных: `CreateCandleImport` возвращает только идентификатор задачи, а ошибки валидации — исключение
  `ValidationFailedException` с ошибками по полям; контроллер отвечает `400`. Запрос `GetCandles` возвращает результат с
  ошибками: запросы данные возвращают. Контроллеры, фоновая служба и служба инициализации вызывают сценарии через
  `ISender`; регистрация MediatR — в `Infrastructure` (`Mediatr/MediatrRegistrar`). MediatR закреплён на 12.5.0: с 13.0
  библиотека распространяется по коммерческой лицензии.
- Уточнено при ревью: исключения слоя — в `Application/Exceptions` (namespace `ProfitFox.Application.Exceptions`):
  абстрактный `BaseApplicationException` и его наследники `ExchangeException` (биржа отклонила запрос или недоступна) и
  `ValidationFailedException` (ошибки параметров по полям). Контроллер ловит `ExchangeException` и отвечает `503`,
  `ValidationFailedException` — `400`.
- `Infrastructure`: публичный `InfrastructureRegistrar` — единая точка регистрации по образцу прежнего проекта:
  MediatR (`Mediatr/MediatrRegistrar`), `PostgresRegistrar`, `TimeProvider`, клиент SDK Bybit (`IBybitRestClient`),
  конвейер Polly (`Exchanges/Retry/ExchangeResilienceExecutor`), очередь и фоновые службы
  `BackgroundServices/CandleImportInitializationService` и `CandleImportBackgroundService`;
  `Messaging/CandleImportQueue` (`Channel<Guid>`), адаптер Bybit на SDK (`Exchanges/Bybit`), и `Persistence/Postgres`:
  `PostgresRegistrar` (`NpgsqlDataSource`, репозитории, раннер FluentMigrator), `FluentMigrator/Migrations` и
  `FluentMigrator/Extensions` (`MigrateToLatestVersion`), `Dapper/Repositories/MarketData` с подпапками `Contracts` (
  строки `*Db`), `Converters` (`*DbConverter`) и `Queries` (SQL, `*RepositoryQueries`).
- Уточнено при ревью: классы `Persistence` — `internal`, `Tests.Integration` видит их через `InternalsVisibleTo`; наружу
  открыты только `InfrastructureRegistrar`, `MigrateToLatestVersion` и сами миграции; `PostgresRegistrar`, фоновая
  служба и очередь — `internal`. Миграции открыты потому, что FluentMigrator находит только `public`-классы миграций (с
  `internal` раннер падает с `No migrations found`). `MigrateToLatestVersion` расширяет `IServiceProvider`, а не
  `IApplicationBuilder`, как в прежнем проекте: так тестовая фикстура применяет миграции без веб-хоста. Строка
  подключения читается при первом обращении, чтобы тесты могли подменить её через `WebApplicationFactory`. Из прежнего
  проекта не перенесены транзакции, настройка размера пула, `ReloadTypes` и обработчики запросов в `Infrastructure`:
  сейчас они ничего не дают, обработчики запросов остаются в `Application`. `Infrastructure` подключает
  `FrameworkReference Microsoft.AspNetCore.App`, как прежний проект: оттуда `BackgroundService`, `AddHostedService` и
  `AddHttpClient`.
- Уточнено при ревью: перевод прерванных задач в `Failed` при старте делает `CandleImportInitializationService` —
  `IHostedService`, а не `BackgroundService`, как `BotRuntimeInitializationBackgroundService` прежнего проекта: хост
  дожидается `StartAsync` до запуска сервера (проверено на Kestrel), а `ExecuteAsync` у `BackgroundService` шёл бы
  параллельно с приёмом запросов и мог бы перевести в `Failed` только что созданную задачу. Миграции применяет `Program`
  до запуска хоста.
- `Presentation`: контроллеры, контракты API, `Program` (вызывает `InfrastructureRegistrar`, применяет миграции через
  `MigrateToLatestVersion`).

### 11. Тесты

- Unit (`Tests.Unit`): конец свечи для каждого вида интервала, фильтр закрытых свечей и границ периода, парсинг ответов
  Bybit на эталонных JSON-фикстурах, пагинация и повторы адаптера на подменённом `HttpMessageHandler`, валидация
  запросов.
- Integration (`Tests.Integration`): `WebApplicationFactory` + TimescaleDB в Testcontainers (образ
  `timescale/timescaledb` с фиксированной версией тега) + подменённый HTTP Bybit. Покрывают upsert/идемпотентность,
  hypertable-миграцию, сценарии API целиком, перевод прерванных задач в `Failed`.
- Ни один тест не ходит в реальный Bybit.
- Уточнено при ревью: тестовая инфраструктура по образцу прежнего проекта. Проект `Tests.Shared` (
  `backend/tests/ProfitFox.Tests.Shared`, ссылки на `Application`, AutoFixture 4.18.1) с генераторами `Generators/Base`
  и `Generators/Domain/{Entity,ValueObject}/MarketData/*Generator.Generate(...)`; его используют оба тестовых проекта.
  Порты в unit-тестах подменяются моками NSubstitute 6.0.0 вместо самописных фейков; в `Tests.Integration` NSubstitute
  подменяет очередь, когда тесту нужно отключить выполнение задач; проверки пишутся на Shouldly 4.3.0. Генераторы дают
  случайные значения только данным, которые тест не проверяет; эталонные данные (точность `decimal`, границы периода,
  закрытость свечи, коды интервалов) задаются явно. Самописными остаются только заглушки транспорта HTTP —
  `StubHttpHandler` в unit-тестах адаптера и `FakeBybitHandler` в интеграционных: NSubstitute не подменяет protected
  `SendAsync`.

### 12. Локальная разработка

`docker-compose.yml` в корне репозитория с сервисом TimescaleDB (тот же тег образа, что в тестах). Пароль — из `.env` (в
`.gitignore`), в git — `.env.example`. Строка подключения сервиса — `ConnectionStrings__MarketData` через переменную
окружения или user secrets.

- Уточнено при реализации: образ `timescale/timescaledb:2.30.2-pg17` (PostgreSQL 17).

## Risks / Trade-offs

- [Bybit меняет формат ответа или лимиты] → парсинг изолирован в адаптере и покрыт фикстурами; лимиты — повторы с
  backoff.
- [`instruments-info` не возвращает делистнутые символы] → историю делистнутых инструментов загрузить нельзя; приемлемо
  для первой версии, при необходимости — отдельное изменение.
- [Миграции при старте и несколько экземпляров сервиса] → сейчас экземпляр один; при масштабировании миграции выносятся
  в отдельный шаг.
- [Последовательный воркер — долгие задачи блокируют очередь] → для ручной загрузки истории приемлемо; параллелизм — при
  появлении автоактуализации.
- [Интеграционные тесты требуют Docker] → Docker уже нужен для dev compose; без Docker запускаются только unit-тесты.
- [Повторная загрузка длинного периода тратит запросы] → см. решение 6.

## Migration Plan

Первое хранилище, данных для переноса нет. При старте сервис применяет миграции к пустой БД. Откат — удалить БД / volume
dev compose. Production не затрагивается.
