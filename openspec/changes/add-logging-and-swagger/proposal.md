# Proposal

## Why

Сервис пишет логи стандартным консольным логгером .NET: на проде в `docker logs` попадает многострочный текст, который нельзя разобрать по полям, а HTTP-запросы не видны совсем. API уже публикует OpenAPI-документ, но только в Development и без UI, поэтому проверить API на проде или локально из браузера нельзя.

## What Changes

- Логирование переходит на Serilog с выводом только в консоль: в Development — читаемый текст, в остальных средах — компактный JSON, одна строка на событие.
- Уровни и формат логов задаются в секции `Serilog` файлов `appsettings*.json` вместо секции `Logging`; шум фреймворка (`Microsoft.AspNetCore`) и мигратора (`FluentMigrator`) отсекается уровнем `Warning`.
- Каждый HTTP-запрос оставляет одну строку лога с методом, путём, статусом и временем ответа.
- Ошибка при запуске сервиса (например, недоступная БД при миграции) записывается в лог до остановки процесса.
- OpenAPI-документ `/openapi/v1.json` публикуется во всех средах, включая Production, а не только в Development.
- Добавляется Swagger UI по адресу `/swagger` поверх этого документа, во всех средах.

## Capabilities

### New Capabilities
- `api-documentation`: публикация описания HTTP API (OpenAPI-документ и Swagger UI) и среды, в которых оно доступно.
- `service-logging`: что и в каком формате сервис пишет в лог — события приложения, HTTP-запросы, ошибки запуска.

### Modified Capabilities
<!-- Нет: требования market-data-import и market-data-query не меняются. -->

## Impact

- `backend/src/ProfitFox.Presentation`: `Program.cs`, `ProfitFox.Presentation.csproj` (пакеты `Serilog.AspNetCore` 10.0.0 и `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1), `appsettings.json`, `appsettings.Development.json`.
- `backend/tests/ProfitFox.Tests.Integration`: тесты публикации документа и UI в среде Production.
- `docs/conventions.md`: правило о регистрации в `Program` уточняется — там же живёт настройка хоста (логирование, документация API).
- Внешних сервисов не добавляется; логи на проде по-прежнему собирает Docker.
- Безопасность: у API нет авторизации, поэтому на проде любой в локальной сети сможет вызывать эндпойнты из браузера. Риск принят владельцем (см. `design.md`).
