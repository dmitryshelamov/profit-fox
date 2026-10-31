# Design

## Context

Мотивация — см. `proposal.md`. Спеков у изменения нет (`skip_specs: true`): поведение системы не меняется, критерии приёмки собраны здесь и в `tasks.md`.

Текущее состояние ветки `feat/add-bybit-market-data` (0121282, с `OutboundHttpGuard` и `docs/conventions.md`), от которой отходит `chore/add-ci-and-analyzers`:
- Решение `backend/ProfitFox.sln`: четыре проекта `backend/src`, три проекта `backend/tests`. Общих `Directory.Build.props`, `.globalconfig`, `global.json` нет; каждый `.csproj` задаёт свойства сам. Локальный SDK — 10.0.302 (Roslyn 5.6.0).
- Интеграционные тесты поднимают TimescaleDB через Testcontainers, им нужен Docker.
- `backend/src` не использует `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` и `Parse` без `IFormatProvider`; время API читается через `DateTimeOffset.TryParseExact(..., CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, ...)`. `backend/tests` широко использует `DateTimeOffset.Parse("...Z")` для эталонных дат.
- Ветка `chore/add-editorconfig` (от `main`) добавляет корневой `.editorconfig` из настроек Rider. Ключей `dotnet_diagnostic.*` и `dotnet_analyzer_diagnostic.*` в нём нет; уровни заданы суффиксами `:error` / `:warning` у опций стиля и у правил именования.
- `origin` — GitHub `dmitryshelamov/profit-fox`, хотя раздел Workflow в `CLAUDE.md` называет GitLab.

Проверено до написания дизайна на копии ветки вне репозитория: с конфигурацией из решений 3–4 сборка, unit-тесты (в том числе с `TZ=Asia/Tokyo`) и `dotnet format --verify-no-changes` проходят без правок кода; добавленные вручную лишний `using` и `DateTime.Now` дают `error IDE0005` и `error RS0030`.

## Goals / Non-Goals

**Goals:**
- Нарушение любого из правил — красная сборка локально и в CI, без отдельного шага и без IDE.
- Уровни правил задаются в файлах, которые не пересекаются с `.editorconfig` ветки `chore/add-editorconfig`.

**Non-Goals:**
- Правило «один тип на файл» в анализаторе (решение 5).
- Защита ветки `main` в настройках GitHub (обязательные проверки) — это настройка репозитория, её делает владелец.
- Матрица ОС и версий SDK в CI.

## Decisions

### 1. CI — один job на `ubuntu-latest`
`.github/workflows/ci.yml` запускается на `push` в `main` и на `pull_request`. Шаги: `actions/checkout@v7`, `actions/setup-dotnet@v6` с `dotnet-version: 10.0.x`, `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build`, `dotnet test --no-build` с `TZ=Asia/Tokyo`, `dotnet format --verify-no-changes --no-restore`. Docker на раннере GitHub уже есть, поэтому Testcontainers работает без настройки.
- Один job вместо матрицы `TZ`: сборка делается один раз, второй прогон тестов использует уже собранные сборки. Цена — прогоны идут последовательно.
- `Asia/Tokyo` (UTC+9, без перехода на летнее время): смещение большое и постоянное, поэтому ошибка «местное время вместо UTC» сдвигает результат на 9 часов и видна в любой день года.
- `concurrency` с отменой устаревших запусков той же ветки — чтобы новый push не ждал старый.
- Версии actions — мажорные теги: `checkout` v7.0.1 и `setup-dotnet` v6.0.0 вышли в июле 2026, больше двух недель назад.
- Уточнено при реализации: `actionlint` не устанавливается (решение владельца). Синтаксис проверен разбором YAML и сверкой ключей с документацией GitHub Actions и `action.yml` обоих actions (`dotnet-version` есть в `setup-dotnet@v6`); окончательная проверка — первый запуск workflow на PR.
- Проверено при реализации: прогон с `TZ=Asia/Tokyo` ловит ошибку часового пояса. С `DateTimeStyles.AssumeLocal` вместо `AssumeUniversal` в `ApiTimestamps` unit-тесты в UTC проходят, а в `Asia/Tokyo` падают 4 теста.

### 2. Pre-commit — build и format, без тестов
`.githooks/pre-commit` (POSIX `sh`, исполняемый) выполняет `dotnet build backend/ProfitFox.sln` и `dotnet format backend/ProfitFox.sln --verify-no-changes --no-restore`, если среди проиндексированных файлов есть что-то под `backend/` или корневой `.editorconfig`; иначе выходит сразу (коммит только с документами OpenSpec не ждёт сборку). Тесты в хук не входят: интеграционные требуют Docker и идут минуты — это работа CI.
- Хук проверяет рабочее дерево, а не только проиндексированный снимок: частично проиндексированный файл может пройти локально и упасть в CI. Принято: хук — быстрая подсказка, барьер — CI.
- Строка `git config core.hooksPath .githooks` в `CLAUDE.md` остаётся и дополняется тем, что делает хук.

### 3. `IDE0005` в обычной сборке
`backend/Directory.Build.props` (общий для всех проектов) задаёт `EnforceCodeStyleInBuild=true`, `GenerateDocumentationFile=true` и `NoWarn` += `CS1591`. Уровень `dotnet_diagnostic.IDE0005.severity = error` — в `backend/.globalconfig` (`is_global = true`), который SDK подхватывает автоматически.
- Без `GenerateDocumentationFile` компилятор не сообщает `IDE0005` при сборке (выяснено на ревью `add-bybit-market-data`); `CS1591` («нет XML-комментария у публичного члена») отключается, потому что документировать каждый публичный член проект не требует.
- `.globalconfig`, а не `.editorconfig`: уровень не пересекается с корневым `.editorconfig` ветки `chore/add-editorconfig`, где ключа `IDE0005` нет. При совпадении ключей `.editorconfig` имеет приоритет над `.globalconfig`, поэтому если `.editorconfig` позже задаст `IDE0005`, победит он — это ожидаемо.
- Альтернатива — `WarningsAsErrors` с `IDE0005` в `Directory.Build.props` — не работает: по умолчанию у `IDE0005` уровень `hidden`, и в предупреждение он не превращается.
- `Directory.Build.props` лежит в `backend/`, а не в корне репозитория: всё .NET-содержимое живёт в `backend/`.

### 4. Запрещённые API — `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.0.0 только в `backend/src`
`backend/src/Directory.Build.props` импортирует `backend/Directory.Build.props`, подключает пакет с `PrivateAssets=all`, добавляет `backend/src/BannedSymbols.txt` как `AdditionalFiles` и переводит `RS0030`, `RS0031`, `RS0035` в `WarningsAsErrors`. Список:
- `P:System.DateTime.Now`, `P:System.DateTime.Today`, `P:System.DateTimeOffset.Now` — «сейчас» берётся из `TimeProvider`.
- `M:System.DateTime.Parse(System.String)`, `M:System.DateTimeOffset.Parse(System.String)` — перегрузки без `IFormatProvider`; перегрузки с провайдером разрешены (проверено: `DateTime.Parse(string, IFormatProvider)` не помечается).
- Только `backend/src`: тесты задают эталонные даты строками с `Z` через `DateTimeOffset.Parse(string)` (≈ 35 мест), это безопасно и читается лучше альтернатив. Часовой пояс в тестах ловит прогон с `TZ=Asia/Tokyo` (решение 1).
- Версия 5.0.0, а не последняя стабильная 5.6.0 (обе от июля 2026). Версии пакета совпадают с версиями Roslyn: 5.6.0 собрана под Roslyn 5.6 (SDK 10.0.3xx), и SDK 10.0.1xx/2xx загрузить её не сможет — анализатор отключится с предупреждением `CS8032`, и запрет молча перестанет действовать. 5.0.0 загружается любым SDK .NET 10, а правила запрета у версий не отличаются.
- `global.json` для закрепления SDK не вводится: с 5.0.0 он не нужен, а закрепление SDK — отдельное решение для всей команды.

### 5. StyleCop.Analyzers не подключается
Задача предлагала `SA1402` (один тип на файл) и `SA1649` (имя файла совпадает с первым типом). Единственная стабильная версия StyleCop.Analyzers — 1.1.118 (апрель 2019). На коде ветки она бросает `AD0001` (`NullReferenceException` в `SA1500`, `SA1502`, `SA1508`) и не сообщает ни `SA1402`, ни `SA1649` в файлах с file-scoped namespace — то есть во всех файлах проекта. `1.2.0-beta.556` (декабрь 2023) на том же коде ловит оба правила, включая `record`, а линейка `1.3.x-beta` вышла в октябре 2026 и моложе двух недель. Владелец выбрал не подключать StyleCop: правило «один тип на файл» остаётся текстовым. В ветке `feat/add-bybit-market-data` оно переезжает из раздела Architecture `CLAUDE.md` в раздел «Files» `docs/conventions.md`; это изменение не трогает ни ту строку `CLAUDE.md`, ни `docs/conventions.md`, чтобы не конфликтовать при слиянии.

## Risks / Trade-offs

- [После слияния `chore/add-editorconfig` опции с `:error` начнут ломать сборку, потому что `EnforceCodeStyleInBuild` применяет уровни `.editorconfig` к сборке. Код ветки Bybit нарушит часть из них (`IDE0008` для `var`, порядок `using`, `IDE1006` для имён полей — по заметкам сессии `.editorconfig`)] → `chore/add-editorconfig` сливается вместе с приведением кода к стилю (владелец уже запланировал этот шаг после слияния), CI покажет точный список нарушений.
- [`GenerateDocumentationFile` включает проверки XML-комментариев (`CS1572`, `CS1573` и др.): кривой `<param>` станет предупреждением] → на текущем коде таких предупреждений нет; если появятся, это настоящие ошибки комментариев.
- [Pre-commit удлиняет коммит на время сборки и `dotnet format` (десятки секунд)] → хук пропускает коммиты без файлов `backend/`; в крайнем случае `git commit --no-verify`, CI всё равно проверит.
- [Запрет покрывает только перечисленные символы: `DateTime.UtcNow`, `TryParse(string, out ...)`, `ParseExact` и перегрузки с провайдером без `DateTimeStyles.AssumeUniversal` по-прежнему могут прочитать время как местное] → это ловит прогон тестов с `TZ=Asia/Tokyo`; список расширяется по мере находок.
- [В ветке `feat/add-bybit-market-data` появляется `OutboundHttpGuard` (`Tests.Shared/Network`): `SetUpFixture` обоих тестовых проектов ставит `HttpClient.DefaultProxy` на `127.0.0.1:9`, и любой исходящий HTTP из тестов падает] → в CI тестам сеть не нужна: образ TimescaleDB тянет демон Docker, а Testcontainers общается с ним через unix-сокет, а не через `HttpClient.DefaultProxy`. Unit-тесты с охранником уже проходят под новыми правилами (в том числе с `TZ=Asia/Tokyo`); интеграционные подтверждаются в задаче 3.2 и первым запуском workflow на PR.
- [`CLAUDE.md` называет хостинг GitLab и merge requests, а `origin` и CI — на GitHub] → CI делается под фактический `origin`; исправление `CLAUDE.md` в этом изменении не делается — это решение владельца о хостинге.

## Migration Plan

1. После слияния каждый разработчик один раз выполняет `git config core.hooksPath .githooks` (уже записано в `CLAUDE.md`).
2. Владелец по желанию делает проверку `CI` обязательной для `main` в настройках GitHub.
3. Откат — удаление добавленных файлов; код проекта не меняется, поэтому откат безопасен.
