# Tasks

Новой логики расчётов нет, поэтому TDD не применяется. Каждое правило проверяется временной порчей кода: нарушение
вносится, сборка падает с ожидаемым кодом, нарушение откатывается, `git status` чист.

## 1. Лишние `using` в сборке

- [x] 1.1 Добавить `backend/Directory.Build.props` (`EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `NoWarn` +=
  `CS1591`) и `backend/.globalconfig` (`is_global = true`, `dotnet_diagnostic.IDE0005.severity = error`) по решению 3;
  `dotnet build backend/ProfitFox.sln` проходит без новых предупреждений
- [x] 1.2 Временно добавить лишний `using` в файл `backend/src` и в файл `backend/tests`; `dotnet build` падает с
  `error IDE0005` в обоих; откатить

## 2. Запрещённые API в `backend/src`

- [x] 2.1 Добавить `backend/src/Directory.Build.props` (импорт родительского,
  `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.0.0 с `PrivateAssets=all`, `AdditionalFiles`, `WarningsAsErrors` для
  `RS0030`, `RS0031`, `RS0035`) и `backend/src/BannedSymbols.txt` с пятью символами решения 4; `dotnet build` проходит,
  предупреждения `CS8032` нет
- [x] 2.2 Временно использовать в `backend/src` по очереди `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`,
  `DateTime.Parse(string)`, `DateTimeOffset.Parse(string)`; каждый даёт `error RS0030`;
  `DateTimeOffset.Parse(string, IFormatProvider)` ошибки не даёт; тот же `DateTime.Now` в `backend/tests` сборку не
  ломает; откатить

## 3. CI

- [x] 3.1 Добавить `.github/workflows/ci.yml` по решению 1 (push в `main`, `pull_request`, `concurrency`, `checkout@v7`,
  `setup-dotnet@v6` с `10.0.x`, restore, build, test, test с `TZ=Asia/Tokyo`, format `--verify-no-changes`); синтаксис
  проверен `actionlint` (установка — с разрешения владельца) или, если владелец против, разбором YAML и сверкой с
  документацией GitHub Actions
- [x] 3.2 Локально повторить шаги workflow: `dotnet test backend/ProfitFox.sln` и
  `TZ=Asia/Tokyo dotnet test backend/ProfitFox.sln` проходят полностью, включая интеграционные тесты с Docker

## 4. Pre-commit и документация

- [x] 4.1 Добавить исполняемый `.githooks/pre-commit` по решению 2; при `core.hooksPath=.githooks` коммит с лишним
  `using` в `backend/` отклоняется, коммит только с файлом вне `backend/` проходит без сборки; проверочные коммиты
  откатить
- [x] 4.2 Дописать в `CLAUDE.md` к строке про `core.hooksPath` (раздел Commands), что хук запускает build и format;
  раздел Architecture `CLAUDE.md` и `docs/conventions.md` не трогать — их меняет PR #2 (решение 5); `git diff CLAUDE.md`
  показывает только строку про хуки

## 5. Итоговая проверка

- [x] 5.1 `dotnet build`, `dotnet test`, `TZ=Asia/Tokyo dotnet test` и
  `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят на ветке; `git status` показывает только файлы
  этого изменения

## Workflow follow-up

- Коммит и push — только с разрешения владельца; PR — по его просьбе. Ветка `chore/add-ci-and-analyzers` сливается после
  `feat/add-bybit-market-data`.
- Первый запуск workflow на PR подтверждает, что Testcontainers работает на раннере GitHub.
- `/opsx:archive` перед слиянием.
