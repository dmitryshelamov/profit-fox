# Proposal

## Why

В репозитории нет ни одного автоматического барьера: у PR нет проверок, git-хуков нет (хотя `CLAUDE.md` велит включить
`.githooks`), лишние `using` и нарушения правил о времени ловятся только глазами. На ревью `add-bybit-market-data` время
без смещения читалось как местное время сервера, и тесты этого не видели, потому что машина работает в UTC; лишние
`using` искали самописным скриптом, который ошибся четыре раза. Барьеры нужны сейчас, пока кода мало и его легко
привести к правилам.

## What Changes

- CI на GitHub Actions (`origin` — GitHub `dmitryshelamov/profit-fox`): на каждый push и pull request — `dotnet build`,
  `dotnet test`, повторный прогон тестов с `TZ=Asia/Tokyo` и `dotnet format --verify-no-changes`.
- Git-хук `.githooks/pre-commit` с быстрыми проверками — build и format; строка про `core.hooksPath` в `CLAUDE.md`
  становится правдой.
- Анализатор запрещённых API (`Microsoft.CodeAnalysis.BannedApiAnalyzers`) только для проектов `backend/src`:
  `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTime.Parse(string)` и `DateTimeOffset.Parse(string)`
  ломают сборку.
- Лишние `using` (`IDE0005`) ломают обычную `dotnet build`, а не только проверку в IDE.

Вне рамок изменения: StyleCop.Analyzers и правила `SA1402` / `SA1649` («один тип на файл») — владелец отказался:
единственная стабильная версия 1.1.118 не работает на коде проекта (см. `design.md`, решение 5), поэтому правило
остаётся текстовым (в PR #2 оно переезжает в `docs/conventions.md`, этот change его не трогает). Также вне рамок:
перевод кода на стиль `.editorconfig` из ветки `chore/add-editorconfig`, покрытие кода, статический анализ безопасности,
публикация артефактов и деплой.

## Capabilities

### New Capabilities

<!-- Нет: изменение касается только инструментов сборки и разработки, поведение системы не меняется (`skip_specs: true`). Критерии приёмки — в `design.md` и `tasks.md`. -->

### Modified Capabilities

<!-- Нет. -->

## Impact

- **Сборка:** новые `backend/Directory.Build.props`, `backend/.globalconfig`, `backend/src/Directory.Build.props` и
  `backend/src/BannedSymbols.txt`; все проекты начинают генерировать XML-документацию (предупреждение `CS1591`
  отключено). Код `backend/src` и `backend/tests` уже соответствует правилам — проверено на ветке
  `feat/add-bybit-market-data`, правок кода не нужно.
- **Внешние зависимости:** NuGet-пакет `Microsoft.CodeAnalysis.BannedApiAnalyzers` (только на этапе сборки, в выходные
  сборки не попадает); GitHub Actions `actions/checkout` и `actions/setup-dotnet`.
- **Процесс:** у PR появляются обязательные проверки CI; локальный коммит идёт дольше на время build и format.
  `CLAUDE.md` обновляется (хуки).
- **Порядок слияния:** ветка отходит от `feat/add-bybit-market-data` и сливается после неё. После слияния
  `chore/add-editorconfig` её правила с уровнем `error` тоже начнут ломать сборку (см. `design.md`, риски).
- **Production:** не затрагивается.
