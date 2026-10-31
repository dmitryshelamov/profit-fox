# Design

## Context

Мотивация — см. `proposal.md`, требования — `specs/api-documentation` и `specs/service-logging`.

Текущее состояние `main` (4166dc2):
- `ProfitFox.Presentation` подключает `Microsoft.AspNetCore.OpenApi` 10.0.10: `AddOpenApi()` и `MapOpenApi()` только при `IsDevelopment()`. UI нет.
- Логирование — стандартный `Microsoft.Extensions.Logging` с секцией `Logging` в `appsettings.json` и `appsettings.Development.json`. В коде логгер используется в одном месте — `CandleImportBackgroundService`.
- `Program.Main` после `Build()` вызывает `app.Services.MigrateToLatestVersion()`, затем `RunAsync()`. Исключение миграции сейчас роняет процесс необработанным исключением без записи в лог.
- Интеграционные тесты поднимают приложение через `ProfitFoxFactory : WebApplicationFactory<Program>`; среда по умолчанию — Development. Factory выполняет `Main` целиком (миграции в тестах идут через него — это видно по `SmokeTests`). Тесты в сборке выполняются последовательно: `Parallelizable` нигде не задан.
- Предыдущий проект владельца (`profit-fox-old`) использовал Serilog с секцией `Serilog` в `appsettings.json`: `MinimumLevel` с переопределениями `Microsoft.AspNetCore: Warning` и `Microsoft.Hosting.Lifetime: Information`, `Enrich: FromLogContext`, Console sink с `CompactJsonFormatter`. Эта настройка переносится; Graylog, свойство `Application` и пара `AddSwaggerGen` + `AddOpenApi` (два генератора документа) — нет.

## Goals / Non-Goals

**Goals:**
- Формат и уровни логов меняются только конфигурацией: в коде нет ветвления по среде.
- Каждое наблюдаемое поведение из спеков, кроме читаемого формата в Development, проверено интеграционным тестом.

**Non-Goals:**
- Внешние приёмники логов (Seq, Graylog, файлы) — отдельное OpenSpec-изменение, если понадобится поиск по логам.
- Авторизация API и закрытие Swagger UI на проде.
- Корреляция запросов (trace id в каждом событии), метрики.
- Фильтр `when (ex is not HostAbortedException)` в `Main`: его требуют только инструменты, которые строят хост и прерывают `Main` (`dotnet ef`, генерация OpenAPI при сборке); в проекте их нет. Если такой инструмент появится, фильтр добавляется вместе с ним.

## Decisions

### 1. Serilog.AspNetCore 10.0.0, только Console sink
`Serilog.AspNetCore` приносит `Serilog.Sinks.Console`, `Serilog.Settings.Configuration` и `Serilog.Formatting.Compact`, поэтому других пакетов не нужно. Хост регистрирует логгер через `builder.Services.AddSerilog((services, lc) => lc.ReadFrom.Configuration(builder.Configuration).ReadFrom.Services(services))`.
- `ReadFrom.Services` подхватывает зарегистрированные в DI `ILogEventSink`: это шов для тестов (решение 6), в рабочей конфигурации таких регистраций нет.
- Альтернатива — `builder.Host.UseSerilog(...)`, как в старом проекте. Поведение то же; `Services.AddSerilog` — текущий рекомендуемый вызов для `WebApplicationBuilder`.
- Версия 10.0.0 — последняя стабильная, линейка совпадает с .NET 10.

### 2. Формат по средам — только в `appsettings*.json`
Секция `Logging` удаляется из обоих файлов. `appsettings.json` содержит секцию `Serilog`: `MinimumLevel` из старого проекта (`Default: Information`, `Microsoft.AspNetCore: Warning`, `Microsoft.Hosting.Lifetime: Information`) плюс `FluentMigrator: Warning`, `Enrich: [FromLogContext]` и `WriteTo` Console с `formatter: Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact`. `appsettings.Development.json` переопределяет только `WriteTo` — Console с `outputTemplate` вида `[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`.
- `FluentMigrator: Warning` добавлен при реализации по решению владельца: на пустой базе FluentMigrator пишет уровнем `Information` весь SQL миграций и по несколько событий на каждую. Цена — в логе не видно, какие миграции применились; при сбое миграции FluentMigrator пишет `Error`, а `Main` — `Fatal` с исключением, применённые версии видны в таблице `VersionInfo`.
- JSON — значение по умолчанию, а текст — исключение для Development: любая новая среда (Staging, Testing) сразу получает структурный лог.
- `CompactJsonFormatter`, а не `RenderedCompactJsonFormatter`: в событии остаются шаблон (`@mt`) и параметры отдельными полями, отрендеренный текст не дублируется. Время (`@t`) Serilog пишет в UTC.
- Переопределение массива `WriteTo` в `appsettings.Development.json` работает по индексу: элемент `0` заменяет элемент `0` базового файла. Поэтому в Development-файле задаётся тот же `Name: Console`, а `Args.formatter` нужно явно обнулить, иначе он останется от базового файла и перекроет `outputTemplate`. Обнуляется значением `"formatter": null`.
- Проверено при реализации ручным запуском на временной TimescaleDB: в Development строки старта хоста и запросов — текстом (`[23:54:45 INF] Serilog.AspNetCore.RequestLoggingMiddleware: HTTP GET /api/market-data/candles responded 200 in 39.3868 ms`); в Production (`--no-launch-profile`, иначе `launchSettings.json` возвращает Development) все строки — JSON, служебных событий `Microsoft.AspNetCore` уровня `Information` нет, есть только `Warning` от `HttpsRedirectionMiddleware` («Failed to determine the https port for redirect.»). Он был и раньше, это не новое поведение. Событие запроса дополнительно несёт `@tr`/`@sp` (trace id и span id) — их добавляет Serilog сам.

### 3. Лог HTTP-запросов — `UseSerilogRequestLogging`
Middleware ставится первым в конвейере, до `UseHttpsRedirection` и Swagger UI, и пишет одно событие на запрос: `HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms`. Порог `Microsoft.AspNetCore: Warning` убирает стандартные события хостинга о начале и конце запроса, поэтому дублей нет.

### 4. Ошибка запуска — try/catch в `Main` со стартовым логгером
Сразу после `WebApplication.CreateBuilder(args)` `Main` создаёт `Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger()` — тот же формат, что у хоста. Остальная часть `Main` обёрнута в `try { ... } catch (Exception ex) { Log.Fatal(ex, "Host terminated unexpectedly."); throw; }`.
- Уточнено при реализации: `finally { Log.CloseAndFlushAsync() }` не добавлен — Console sink пишет синхронно, буфера нет, и ни один тест его не требует. Он понадобится вместе с первым асинхронным или буферизующим приёмником (Seq, файл).
- `AddSerilog` с `preserveStaticLogger: false` (по умолчанию) при построении хоста заменяет `Log.Logger` логгером хоста. Ошибка миграции происходит после `Build()`, поэтому `Log.Fatal` уходит в логгер хоста — вместе с тестовым `ILogEventSink` (решение 6).
- `throw;` сохраняет ненулевой код выхода и стандартный вывод .NET о необработанном исключении; `WebApplicationFactory` получает исключение и роняет тест, а не висит.
- Стартовый логгер — обычный `CreateLogger()`, не `CreateBootstrapLogger()`. `ReloadableLogger` замораживается при первом построении хоста, и второй `WebApplicationFactory` в том же процессе падает с `InvalidOperationException: The logger is already frozen` — а интеграционные тесты создают factory на каждый тест.
- Исключение до `CreateBuilder` (например, битый JSON в `appsettings.json`) в лог Serilog не попадёт, только в стандартный вывод .NET. Принято: конфигурация ещё не прочитана, писать не во что.
- Уточнено при реализации: стартовый логгер нужен только для ошибок внутри `Build()` (регистрация сервисов, валидация DI) — после `Build()` `Log.Logger` уже логгер хоста. Поэтому он покрыт отдельным тестом: factory, чья регистрация сервисов бросает исключение, и проверка, что текст исключения попал в консоль.

### 5. Swagger UI — `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1 поверх встроенного документа
Документ по-прежнему строит `Microsoft.AspNetCore.OpenApi`; `MapOpenApi()` выносится из `if (IsDevelopment())` и работает во всех средах. UI подключается `app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ProfitFox API"))` и по умолчанию живёт по `/swagger`.
- Только пакет UI, без `Swashbuckle.AspNetCore` целиком: генератор Swashbuckle (`AddSwaggerGen`) дублировал бы встроенный, как в старом проекте.
- Альтернатива — Scalar (`Scalar.AspNetCore`). Владелец просил именно Swagger; Swagger UI привычнее и умеет «Try it out».
- UI во всех средах, включая Production, — решение владельца (см. «Risks»). Условие по среде уходит из `Program` совсем, поэтому ветки, которую нужно покрывать тестом, не остаётся.

### 6. Тесты — через `ProfitFoxFactory`
Интеграционные тесты в `Tests.Integration/Api` и `Tests.Integration` (по месту поведения):
- `ProfitFoxFactory` получает `init`-свойства `Environment` (по умолчанию не задаётся — остаётся Development) и `LogSink`: при заданном `LogSink` factory регистрирует его в DI как `ILogEventSink`, и `ReadFrom.Services` подключает его к логгеру хоста.
- Документ и UI: в среде Production `GET /openapi/v1.json` → `200` и путь `/api/market-data/candles` в документе; `GET /swagger/index.html` → `200`, HTML. Swagger UI 10.x читает конфигурацию из `/swagger/index.js` — тест проверяет, что в нём есть `/openapi/v1.json`.
- Лог запроса: `GET /api/market-data/candles` с валидными параметрами → в `LogSink` ровно одно событие (из событий с `StatusCode`: `RequestPath` из scope запроса через `FromLogContext` есть у всех событий внутри запроса) с `RequestMethod = GET`, `RequestPath = /api/market-data/candles`, `StatusCode = 200` и свойством `Elapsed`.
- Ошибка запуска: factory со строкой подключения на закрытый порт (`127.0.0.1:1`) → `CreateClient()` бросает исключение, а в `LogSink` есть событие уровня `Fatal` с исключением. Ошибка внутри `Build()` — через перехват консоли (решение 4).
- JSON вне Development: в среде Production тест на время запуска перехватывает `Console.Out` (`Console.SetOut`) и проверяет, что каждая непустая строка разбирается как JSON-объект с полями `@t` и `@mt`. Перехват безопасен, потому что тесты идут последовательно. Проверено при реализации: Console sink берёт `Console.Out` при каждой записи, перехват работает.
- Читаемый текст в Development — только ручная проверка `dotnet run`: это значение конфигурации, кода под ним нет.
- Каждый тест сначала красный: для документа и UI — из-за условия `IsDevelopment()` и отсутствия UI, для логов — из-за отсутствия Serilog.

### 7. `docs/conventions.md`: что можно настраивать в `Program`
Правило «`Program` calls `InfrastructureRegistrar.Configure` and `MigrateToLatestVersion`; there is no other registration» уже не соответствует коду (`AddControllers`, `AddOpenApi`). Оно переформулируется: регистрация сервисов приложения идёт только через `InfrastructureRegistrar`; в `Program` остаётся настройка самого хоста — контроллеры, логирование, документация API и конвейер middleware.

## Risks / Trade-offs

- [Swagger UI и документ в Production без авторизации: любой в локальной сети видит все эндпойнты и может вызвать их из браузера, включая будущие торговые] → Риск принят владельцем. Авторизация API должна быть решена до появления эндпойнтов, которые меняют состояние на бирже; это отдельное изменение.
- [На проде переменные окружения `Logging__*` (если они заданы в `docker-compose.prod.yml` или `.env.prod*`) перестанут действовать] → Перед выкаткой владелец проверяет прод-конфигурацию и переводит такие переопределения на `Serilog__MinimumLevel__*`. Агент эти файлы без разрешения не читает.
- [Перехват `Console.Out` в тесте зависит от того, как Console sink получает поток] → Запасной вариант — ручная проверка (решение 6).
- [`docker logs` — единственный способ читать логи на проде, поиска по полям нет] → Принято; JSON в одну строку позволяет фильтровать через `jq`, а Seq можно добавить позже без изменения кода логирования.

## Migration Plan

Обычная выкатка образа: схема БД и API не меняются. Перед выкаткой — проверка прод-переопределений `Logging__*` (см. «Risks»). Откат — предыдущий образ; данных, зависящих от изменения, нет.
