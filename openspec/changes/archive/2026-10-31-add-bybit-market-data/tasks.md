# Tasks

Новая логика — строго red → green → refactor (см. `CLAUDE.md`): сначала падающий тест, потом минимальный код.

## 1. Окружение и зависимости

- [x] 1.1 Удалить шаблонный код скаффолда (`WeatherForecast*`, `Class1.cs`, `UnitTest1.cs`, шаблонный `.http`) и проверить, что `dotnet build backend/ProfitFox.sln` проходит
- [x] 1.2 Добавить `docker-compose.yml` с TimescaleDB (фиксированный тег), `.env.example`, `.env` в `.gitignore`; проверить, что `docker compose up -d` поднимает БД и `psql` видит расширение `timescaledb`
- [x] 1.3 Добавить NuGet-пакеты (Npgsql EF Core provider — позже заменён на Npgsql, Dapper и FluentMigrator в задаче 4.5, `Microsoft.Extensions.TimeProvider.Testing`, Testcontainers.PostgreSql, `Microsoft.AspNetCore.Mvc.Testing`) и ссылки тестовых проектов на нужные слои; проверить сборку
- [x] 1.4 Создать в `Tests.Integration` общий фикстур: TimescaleDB в Testcontainers + `WebApplicationFactory` с подменой HTTP Bybit; проверить пустым smoke-тестом, что контейнер стартует и приложение поднимается

## 2. Доменная модель

- [x] 2.1 `MarketCategory`, `Instrument`, `Interval` с разбором кодов Bybit (`spot`/`linear`, `1`…`720`, `D`, `W`, `M`) и отказом для неподдерживаемых; unit-тесты на каждый код и на `inverse` / `2`
- [x] 2.2 Конец свечи по open time для минутных интервалов, `D`, `W` (7 суток) и `M` (следующий календарный месяц, включая переход через год и февраль); unit-тесты на эталонных датах
- [x] 2.3 `Candle` и правило «закрытая свеча» относительно заданного момента; unit-тест сценария «Период включает текущий момент» из спека
- [x] 2.4 Раскладка домена по `Entities/MarketData`, `Entities/MarketData/Enums` и `ValueObjects/MarketData` с namespace по папкам (решение 10); build, тесты и format проходят
- [x] 2.5 Value objects `Price` и `Volume` — обёртки над `decimal` без инвариантов; `Candle` использует их, адаптеры переводят в `decimal` и обратно (решение 10). Поведение не меняется: точность значений проверяют существующие тесты 3.1, 4.2 и 6.3
- [x] 2.6 `AggregateRoot` в `Entities/Base` и `IDomainEvent` в `Events` (решение 10); unit-тесты: добавленное событие видно в `DomainEvents`, список нельзя изменить снаружи, `ClearDomainEvents` очищает его
- [x] 2.7 Перенести порт `ICandleStore` в `Domain/Repositories` (решение 10); поведение не меняется, build, тесты и format проходят
- [x] 2.8 Перенести `CandleImportJob` в `Entities/MarketData`, `CandleImportStatus` в `Entities/MarketData/Enums`, `ICandleImportJobStore` в `Domain/Repositories` (решение 10); поведение не меняется, build, тесты и format проходят
- [x] 2.9 `Interval` — перечисление в `Entities/MarketData/Enums` с доменным `CandleEnd`; `MarketCategoryCode` и разбор кодов уходят из домена: коды API и колонок БД — `Application/MarketData/MarketDataCodes`, коды Bybit — `Infrastructure/Bybit/BybitCodes` (решение 10); unit-тесты на каждый код в обе стороны и на отказ для неподдерживаемых. Схема, API и данные в БД не меняются: build, тесты и format проходят

## 3. Адаптер Bybit

- [x] 3.1 Парсинг ответа `/v5/market/kline` в свечи с точными `decimal`; unit-тесты на JSON-фикстурах, включая `"0.000123"` и ненулевой `retCode`
- [x] 3.2 Постраничная выгрузка от `to` к `from` (решение 2) с отдачей страниц потребителю; unit-тест на подменённом `HttpMessageHandler` с тремя страницами и пустой последней
- [x] 3.3 Повторы с backoff на 429, 5xx, таймаут и `retCode` 10006, немедленная ошибка на прочих `retCode` (решение 7); unit-тесты с `FakeTimeProvider`, включая исчерпание повторов
- [x] 3.4 Проверка символа через `/v5/market/instruments-info` (найден / не найден / Bybit недоступен); unit-тесты на фикстурах
- [x] 3.5 Адаптер Bybit на SDK `CryptoClients.Net` (решения 1 и 7): `Exchanges/Bybit/BybitMarketDataClient` через `IBybitRestClient`, `BybitCodes` в перечисления SDK, `BybitKlineParser` удаляется; повтор и circuit breaker — `Exchanges/Retry/ExchangeResilienceExecutor` на Polly; лимитер SDK включён. Тесты адаптера остаются на подменённом HTTP; на circuit breaker — новые тесты по TDD (размыкание, отказ без запроса, восстановление после паузы). Build, тесты и format проходят

## 4. Хранилище

- [x] 4.1 `DbContext`, сущности свечи и задачи, первая миграция с `CREATE EXTENSION timescaledb` и `create_hypertable`; интеграционный тест проверяет, что `candles` — hypertable
- [x] 4.2 Upsert страницы свечей одним запросом (решение 4); интеграционные тесты сценариев «Повторная загрузка», «Один символ в разных категориях» и точности `numeric`
- [x] 4.3 Чтение свечей за `[from, to)` по возрастанию с `limit`; интеграционный тест на границы периода и лимит
- [x] 4.4 Хранилище задач: создание, смена статуса, счётчик свечей, причина ошибки, перевод `Pending`/`Running` в `Failed`; интеграционные тесты
- [x] 4.5 Заменить EF Core на FluentMigrator и Dapper (решение 4): миграция `Initial` с той же схемой, расширением и hypertable; `CandleStore` и `CandleImportJobStore` на Dapper; применение миграций при старте через `IMigrationRunner`; удалить EF Core, `dotnet-ef` и EF-миграции; обновить README. Поведение не меняется: интеграционные тесты 4.1–4.4 и 6.1–6.4 проходят; фикстура БД и проверки строк в тестах переходят с `DbContext` на SQL
- [x] 4.6 Переложить хранилище в `Infrastructure/Persistence/Postgres` по образцу прежнего проекта (решение 10): `PostgresRegistrar`, `FluentMigrator/{Migrations,Extensions}`, `Dapper/Repositories/MarketData/{Contracts,Converters,Queries}`; переименовать порты в `ICandleRepository` и `ICandleImportJobRepository`; классы `Persistence` — `internal` с `InternalsVisibleTo` для `Tests.Integration`. Поведение не меняется: build, все тесты и format проходят
- [x] 4.7 Переписать миграцию `CreateMarketDataTablesMigration` на raw SQL (`Execute.Sql`) (решение 4); схема БД не меняется, build, тесты и format проходят

## 5. Сценарии приложения

- [x] 5.1 Создание задачи: валидация параметров, `to` по умолчанию из `TimeProvider`, проверка символа, статус `Pending`, постановка в очередь; unit-тесты на каждый сценарий валидации из спека
- [x] 5.2 Выполнение задачи: `Running` → выгрузка → фильтр закрытых свечей и `[from, to)` → upsert постранично → `Succeeded` / `Failed` с сохранением уже записанного; unit-тесты на фейковых портах, включая «Нет данных за весь период» и «Ошибка биржи»
- [x] 5.3 Пометка прерванных задач при старте; unit-тест
- [x] 5.4 Перевести `Application` на CQRS с MediatR (решение 10): команды `CreateCandleImport`, `RunCandleImport`, `FailInterruptedImports` и запросы `GetCandles`, `GetCandleImport` вместо `CandleImportService` и `CandleQueryService`; контроллеры, воркер и старт вызывают их через `ISender`. Поведение не меняется: unit-тесты 5.1–5.3 вызывают обработчики, интеграционные тесты 6.1–6.4 проходят без изменений
- [x] 5.5 `BaseApplicationException` в `Application/Exceptions` и перенос туда `BybitException` с наследованием от него (решение 10); поведение не меняется, build, тесты и format проходят
- [x] 5.6 Команды без возвращаемых данных (решение 10): `CreateCandleImport` возвращает только id, ошибки валидации — `ValidationFailedException`, контроллер отвечает `400` и читает тело `POST` через `GetCandleImport`; тест «Корректный запрос» отключает выполнение задач. Формат API не меняется: build, тесты и format проходят
- [x] 5.7 Нейтральный порт источника данных (решение 10): `IBybitMarketDataClient` → `IMarketHistoryClient` и `ICandleImportQueue` — в `Application/Ports/MarketData`; `BybitException` → `ExchangeException`; `BybitMarketDataClient` реализует порт. Поведение не меняется: build, тесты и format проходят
- [x] 5.8 Один файл — один тип (правило `CLAUDE.md`): `GetCandlesResult` — в свой файл; после 2.9 в решении нет файлов с несколькими верхнеуровневыми типами

## 6. HTTP API и хостинг

- [x] 6.1 `BackgroundService` с `Channel<Guid>`, применение миграций и пометка прерванных задач при старте, регистрация зависимостей и конфигурации Bybit/БД; интеграционный тест «Перезапуск во время загрузки»
- [x] 6.2 `POST /api/market-data/candle-imports` и `GET /api/market-data/candle-imports/{id}` с `ProblemDetails` (400/404/503); интеграционные тесты сценариев создания, валидации, неизвестного символа, недоступного Bybit и неизвестной задачи
- [x] 6.3 `GET /api/market-data/candles` с валидацией и `decimal` строками в JSON; интеграционные тесты сценариев `market-data-query`
- [x] 6.4 Сквозной интеграционный тест: `POST` → ожидание `Succeeded` → `GET candles` возвращает эталонный набор с подменённого Bybit, включая отброшенную незакрытую свечу
- [x] 6.5 Обновить `README.md`: запуск dev compose, настройка `ConnectionStrings__MarketData`, примеры запросов API; проверить, что команды из README выполняются как написано
- [x] 6.6 Перенести воркер и очередь в Infrastructure по образцу прежнего проекта (решение 10): `BackgroundServices/CandleImportBackgroundService`, `Messaging/CandleImportQueue`, регистрация через `InfrastructureRegistrar`; поведение не меняется, build, тесты и format проходят
- [x] 6.7 Перенести регистрацию и подготовку при старте из `Presentation/MarketData/MarketDataSetup` в Infrastructure по образцу прежнего проекта (решение 10): `InfrastructureRegistrar`, `Mediatr/MediatrRegistrar`, `CandleImportInitializationService` (`IHostedService`); `FrameworkReference Microsoft.AspNetCore.App` вместо `Hosting.Abstractions`; поведение не меняется, build, тесты и format проходят
- [x] 6.8 Фоновая служба не падает на сбое задачи (решение 5): ошибка логируется, следующая задача выполняется; unit-тест по TDD
- [x] 6.9 Время со смещением и обязательные поля (решение 9): `from`/`to` строками с разбором в обработчиках, `[Required]` вместо `required` в контрактах; тесты по TDD на сценарии «Время без смещения» и «Пропущен обязательный параметр» в обоих API

## 7. Финальная проверка

- [x] 7.1 Пройти по диффу: каждая ветка и проверка в новом коде требуется каким-то тестом; лишнее удалить
- [x] 7.2 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` и `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят
- [x] 7.3 Тестовая инфраструктура по образцу прежнего проекта (раздел 11): проект `Tests.Shared` с генераторами на AutoFixture; моки NSubstitute вместо `Fakes.cs`; проверки на Shouldly во всех тестах; `StubHttpHandler` и `FakeBybitHandler` остаются. Набор и смысл тестов не меняются: build, все тесты и format проходят
- [x] 7.4 Замечания ревью без изменения поведения: тест повтора при отказе соединения, тесты «проверка символа — одна попытка», `.env.*` в `.gitignore`, расхождения в README и `tasks.md` 1.3

## Workflow follow-up

- `/opsx:archive` перед merge request, чтобы код и `openspec/specs` попали в `main` одним squash-коммитом.
- MR с заголовком в формате `feat(market-data): ...`.
