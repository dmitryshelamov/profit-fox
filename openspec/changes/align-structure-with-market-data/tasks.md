# Tasks

Команды бэкенда запускаются из `backend/`: `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx`;
интеграционным тестам нужен Docker. Каждая группа кончается сборкой без ошибок и без предупреждений сверх базы, зелёным
прогоном всех тестов с числом, равным базе, и коммитом. Во всём изменении не правятся ожидания тестов, файлы эталонов, имена
типов и членов и логика кода. Файлы переносятся через `git mv`.

«Сверка переноса» в проверках ниже — скрипт, который для каждого перенесённого `.cs` сравнивает файл на `f5bc916` и новый
файл после удаления строк `using`, `namespace` и пустых строк над первой строкой кода; допустимые расхождения — только
перечисленные в design.md, решения 3, 10 и 11.

## База

Снято 2026-11-21 на `f5bc916` до правок, Мак владельца, .NET SDK 10.0.302, Docker 29.9.3.

- `git merge-base --is-ancestor f5bc916 HEAD` — успех.
- `dotnet test ProfitFox.slnx`: unit — 251 прошёл из 251, интеграционные — 162 из 162, упавших и пропущенных нет.
- `dotnet build ProfitFox.slnx --configuration Release`: 0 предупреждений, 0 ошибок.
- Проверка однострочных `<summary>` из `ci.yml`: совпадений нет.
- SHA-256 `frontend/src/api/generated/schema.d.ts`:
  `56f993fcfd20a65f52df17cb56bfe0460173333c55cfba7913250153a631a3c5`.
- Повторяющиеся имена типов в решении (с кодом, сгенерированным из `.proto`): `Program` (три проекта), `RunLedgerTests` (два
  проекта тестов), `TimeRange` (Domain и `.proto`; где нужны оба, тип назван полным именем).

## 1. Предложение

- [ ] 1.1 Записать раздел «База» и закоммитить это изменение OpenSpec отдельным коммитом. Проверить:
  `openspec validate align-structure-with-market-data --strict` проходит.

## 2. Файлы сборки

- [ ] 2.1 `backend/Directory.Build.props` и `backend/Directory.Packages.props` по design.md, решение 1; из всех `.csproj` убрать
  общие свойства и атрибуты `Version`; добавить новые файлы в папку `/common/` `ProfitFox.slnx`. Проверить: сборка и тесты
  зелёные, число тестов равно базе; версии в `Directory.Packages.props` совпадают с версиями из `.csproj` на `f5bc916`, новых
  пакетов нет; `dotnet list ProfitFox.slnx package` показывает те же пакеты и версии, что на `f5bc916`.
- [ ] 2.2 `backend/Directory.Build.targets` — правило ссылок слоёв со списками design.md, решение 2. Проверить: сборка
  зелёная; временная ссылка `ProfitFox.Domain` → `ProfitFox.Application` роняет сборку сообщением
  `ProfitFox.Domain may reference only [], not ProfitFox.Application`, после отката сборка снова зелёная.
- [ ] 2.3 `.editorconfig` в корне и `backend/.globalconfig` по design.md, решение 1. Проверить: сборка показывает замечания
  из таблицы решения 3 и никаких других.
- [ ] 2.4 Поправить замечания решения 3 отдельным коммитом. Проверить: сборка — 0 предупреждений, 0 ошибок; тесты зелёные,
  число равно базе; дифф коммита содержит только удалённые `using`, объявления `var`/явного типа и два тега `<param>`.

## 3. Domain

- [ ] 3.1 Перенести файлы `ProfitFox.Domain` по таблице design.md, решение 5; `namespace` — за папкой; `using` во всех
  проектах — по решению 11. Проверить: сборка и тесты зелёные, число равно базе; сверка переноса пуста; `git diff -M --stat`
  показывает переименования; модификаторы доступа не изменились (решение 9).

## 4. Application

- [ ] 4.1 Перенести файлы `ProfitFox.Application` по таблице design.md, решение 6. Проверить: сборка и тесты зелёные, число
  равно базе; сверка переноса пуста.

## 5. Infrastructure

- [ ] 5.1 Перенести файлы `ProfitFox.Infrastructure` по таблице design.md, решение 7, вместе с `.proto`; поправить
  `csharp_namespace` и путь `Protobuf Include`. Проверить: сборка и тесты зелёные, число равно базе; сверка переноса пуста;
  дифф `.proto` — одна строка `csharp_namespace` в каждом.

## 6. Presentation, Worker, Cli

- [ ] 6.1 Перенести файлы `ProfitFox.Presentation`, `ProfitFox.Worker`, `ProfitFox.Cli` по таблицам design.md, решение 8.
  Проверить: сборка и тесты зелёные, число равно базе; сверка переноса пуста.

## 7. Тесты

- [ ] 7.1 Перенести файлы `ProfitFox.Tests.Unit` и `ProfitFox.Tests.Integration` по таблицам design.md, решение 10, вместе с
  папками эталонов; поправить строки путей к эталонам и полные имена типов (решение 11). Проверить:
  - тесты зелёные, число unit- и интеграционных равно базе;
  - сверка переноса пуста, кроме строк путей к эталонам в четырёх тестах решения 10;
  - файлы `Fixtures` перенесены без изменений содержимого (`git diff -M` показывает их как переименования на 100 %),
    `Tests.Integration/Fixtures` не тронута;
  - `BenchDatabase` в корне проекта, его `namespace` — `ProfitFox.Tests.Integration`.

## 8. Документы

- [ ] 8.1 Обновить раздел «Слои» в `README.md`: раскладка «сначала вид, потом тема», файлы сборки, правило ссылок, граница
  расчёта по решению 9. Проверить: каждый путь и имя в разделе существуют в репозитории; `Dockerfile`,
  `.claude/launch.json` и `ci.yml` указывают на существующие проекты.

## 9. Итоговая проверка

- [ ] 9.1 `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx` из `backend/`. Проверить: 0
  ошибок, предупреждений не больше базы; unit — 251, интеграционные — 162; проверка однострочных `<summary>` из `ci.yml`
  проходит.
- [ ] 9.2 Результаты расчёта прежние: `EngineVersion.Number` равен 2, `EngineVersions.json` совпадает с `f5bc916` байт в
  байт, `EngineVersionTests` и `H0001ParityTests` зелёные.
- [ ] 9.3 API прежнее: запустить API на свободном порту (не 5240) и выполнить `npm --prefix frontend run generate:api`.
  Проверить: SHA-256 `schema.d.ts` равен базе.
- [ ] 9.4 Образы: `docker build --file backend/src/ProfitFox.Presentation/Dockerfile .` и
  `docker build --file backend/src/ProfitFox.Worker/Dockerfile .` из корня собираются.
- [ ] 9.5 Сверка переноса по всей ветке против `f5bc916` пуста, кроме мест решений 3, 10, 11. Проверить:
  `openspec validate align-structure-with-market-data --strict` проходит, все задачи отмечены, «Открыто после применения»
  заполнен.

## Workflow follow-up

- Push, пул-реквест, слияние и архив изменения — по слову владельца, после слияния `merge-bench-and-bot`.

## Открыто после применения

Заполняется при применении.
