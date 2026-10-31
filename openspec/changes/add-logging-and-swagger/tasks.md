# Tasks

Новое поведение — строго red → green → refactor (см. `CLAUDE.md`): сначала падающий тест, потом минимальный код.

## 1. Зависимости и тестовый шов

- [ ] 1.1 Добавить в `ProfitFox.Presentation.csproj` пакеты `Serilog.AspNetCore` 10.0.0 и `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1; проверить, что `dotnet build backend/ProfitFox.sln` проходит
- [ ] 1.2 Добавить в `ProfitFoxFactory` `init`-свойства `Environment` (не задано — среда по умолчанию) и `LogSink` (регистрируется в DI как `ILogEventSink`) (design, решение 6); поведение существующих тестов не меняется — `dotnet test` проходит

## 2. Документация API

- [ ] 2.1 Red: интеграционный тест «Документ в Production» — в среде Production `GET /openapi/v1.json` отвечает `200`, в документе есть путь `/api/market-data/candles`; убедиться, что он падает с `404`. Green: убрать условие `IsDevelopment()` вокруг `MapOpenApi()`; тест зелёный
- [ ] 2.2 Red: интеграционные тесты «Страница в Production» и «Страница указывает на документ API» — `GET /swagger/index.html` отвечает `200` с HTML, ресурс инициализации UI ссылается на `/openapi/v1.json`; убедиться, что они падают с `404`. Green: `UseSwaggerUI` с `SwaggerEndpoint("/openapi/v1.json", ...)` (design, решение 5); тесты зелёные

## 3. Логирование

- [ ] 3.1 Red: интеграционный тест «Событие в Production» — в среде Production каждая непустая строка перехваченного `Console.Out` за время запуска разбирается как JSON с полями `@t` и `@mt`; убедиться, что он падает на текстовом выводе стандартного логгера. Green: `AddSerilog` с `ReadFrom.Configuration`, секция `Serilog` в `appsettings.json` вместо `Logging` (`MinimumLevel` с переопределениями, `Enrich: FromLogContext`, Console с `CompactJsonFormatter`) (design, решения 1–2); тест зелёный. Если перехват не работает с Console sink — заменить тест ручной проверкой и записать это в `design.md` (решение 6)
- [ ] 3.2 Red: интеграционный тест «Успешный запрос» — после `GET /api/market-data/candles` в `LogSink` ровно одно событие с `RequestMethod`, `RequestPath`, `StatusCode = 200` и `Elapsed`; убедиться, что он падает (событий нет). Green: `ReadFrom.Services` и `UseSerilogRequestLogging` первым в конвейере (design, решения 1 и 3); тест зелёный
- [ ] 3.3 Red: интеграционный тест «База недоступна при миграции» — factory со строкой подключения на несуществующий хост: `CreateClient()` бросает исключение, в `LogSink` есть событие `Fatal` с исключением; убедиться, что он падает (события нет). Green: стартовый логгер и `try / catch / throw / finally CloseAndFlushAsync` в `Main` (design, решение 4); тест зелёный
- [ ] 3.4 Переопределить в `appsettings.Development.json` только `WriteTo` на Console с текстовым `outputTemplate`, удалить там секцию `Logging` (design, решение 2); проверить вручную `dotnet run --launch-profile http`: строки старта хоста (`Now listening on`) и запроса — текстом, не JSON; запуск с `ASPNETCORE_ENVIRONMENT=Production` — JSON в одну строку; служебных событий `Microsoft.AspNetCore` уровня `Information` нет. Результат проверки записать в `design.md`, решение 2

## 4. Документация

- [ ] 4.1 Переформулировать в `docs/conventions.md` правило о регистрации в `Program` (design, решение 7); `dotnet format --verify-no-changes` проходит
- [ ] 4.2 Проверить `README.md` на упоминания логов, OpenAPI и Swagger; дописать адрес Swagger UI (`/swagger`) и то, что формат логов задаётся секцией `Serilog` в `appsettings*.json`

## 5. Финальная проверка

- [ ] 5.1 Пройти по диффу: каждая ветка и проверка в новом коде требуется каким-то тестом; `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` и `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят

## Workflow follow-up

- Владелец перед выкаткой проверяет прод-конфигурацию на переопределения `Logging__*` и переводит их на `Serilog__MinimumLevel__*` (design, «Risks»).
- `/opsx:archive` перед merge request; MR — squash с заголовком `feat: add serilog logging and swagger ui`.
