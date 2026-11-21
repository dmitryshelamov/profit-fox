# Tasks

Тесты бэкенда запускаются из `backend/`: `dotnet test ProfitFox.slnx`; интеграционным нужен Docker. Каждая группа
заканчивается сборкой без ошибок, зелёным прогоном всех тестов и коммитом. Во всём изменении не правятся ожидания тестов,
файлы эталонов и логика кода: меняются расположение файлов, `namespace`, `using`, ссылки между проектами и строки путей.
Файлы переносятся через `git mv`.

## 1. База для сравнения и удаление шаблонов бота

- [x] 1.1 Проверить, что изменение начато от `release/0.1.0` со слитым `add-strategy-graph`:
  `git merge-base --is-ancestor d8b2e08 HEAD` завершается успешно. Записать в конец этого файла раздел «База» с числами до
  правок: сколько unit- и интеграционных тестов прошло, сколько предупреждений у
  `dotnet build ProfitFox.slnx --configuration Release`, контрольная сумма
  `frontend/src/api/generated/schema.d.ts`. Поднять базу разработки `docker compose up -d --wait bench-db` и один раз
  запустить API до правок, чтобы база была мигрирована прежней сборкой. Проверить: раздел есть, все тесты зелёные,
  `/health` отвечает успехом.
- [x] 1.2 Удалить пять проектов `backend/src/bot/ProfitFox.Bot.*` и папку решения `/src/bot/` из `ProfitFox.slnx`.
  Проверить: сборка и все тесты зелёные, число тестов равно базе, предупреждений сборки не больше базы, каталога
  `backend/src/bot` нет.

## 2. Одна зона

- [x] 2.1 Перенести шесть проектов `backend/src/bench/ProfitFox.Bench.*` в `backend/src/ProfitFox.*` по таблице design.md,
  решение 3: каталоги, файлы проектов, ссылки между проектами и из тестов, `ProfitFox.slnx` (одна папка `/src/`),
  пространства имён в коде и в тестах, `csharp_namespace` в `.proto`. Сборка CLI остаётся `bench`. Проверить: сборка и все
  тесты зелёные, число тестов равно базе; `git diff -M --stat` показывает переименования файлов, а не удаления с
  добавлениями.
- [x] 2.2 Привести к новым путям оба `Dockerfile` (пути `dotnet publish`, имя сборки в `ENTRYPOINT`), `.claude/launch.json`
  (пути проектов; имена конфигураций прежние) и команды раздела «Стенд на Маке» в `README.md`. Проверить:
  `docker build --file backend/src/ProfitFox.Presentation/Dockerfile .` и
  `docker build --file backend/src/ProfitFox.Worker/Dockerfile .` из корня репозитория собираются; каждая команда
  `dotnet run --project …` из `README.md` указывает на существующий проект.
- [x] 2.3 Проверить запуск на прежней базе (design.md, решения 7 и 9): на базе из 1.1, мигрированной прежней сборкой,
  запустить API из `backend/src/ProfitFox.Presentation`. Проверить: `/health` отвечает успехом, а версия схемы в таблице
  версий та же, что до запуска, и новых строк в ней нет. Миграцию пустой базы покрывают интеграционные тесты базы.

## 3. Движок в Domain

- [x] 3.1 Перенести папки `Graph`, `Nodes`, `Execution` из `ProfitFox.Engine.Strategy` и `Trades`, `Positions` из
  `ProfitFox.Engine.Simulation` в `ProfitFox.Domain` с их вложенностью; пространства имён — по таблице design.md,
  решение 3; модификаторы доступа не менять. Удалить оба проекта движка, их ссылки и папку решения `/src/engine/`.
  Проверить: сборка и все тесты зелёные, число тестов равно базе, каталога `backend/src/engine` нет.
- [x] 3.2 Проверить границу расчёта (design.md, решение 2): в `ProfitFox.Domain` вне папок `Graph`, `Nodes`, `Execution`,
  `Trades`, `Positions` нет обращений к типам, которые в этих папках объявлены `internal`. Проверить поиском по именам
  этих типов; результат — пустой список — записать в сообщение коммита группы.
- [x] 3.3 Обновить раздел о слоях в `README.md`: один набор слоёв, расчёт (граф, узлы, сделка, позиция) лежит в Domain,
  зон `bench`, `engine`, `bot` больше нет; «версия движка» — номер версии расчёта. Проверить: каждый путь и имя проекта в
  разделе существуют в репозитории.

## 4. Тесты вслед за кодом

- [x] 4.1 Перенести папки `ProfitFox.Tests.Unit` и `ProfitFox.Tests.Integration` по таблице design.md, решение 5, вместе с
  их папками эталонов; поправить `namespace`, `using`, строки путей к эталонам и шаблоны включения файлов в проектах
  тестов. Проверить:
  - все тесты зелёные, число unit- и интеграционных тестов равно базе;
  - `git diff -M` по `backend/tests` не содержит изменённых строк, кроме `namespace`, `using`, путей к папкам эталонов и
    упоминаний имён проектов в комментариях;
  - файлы в папках `Fixtures` перенесены без изменений содержимого, `Tests.Integration/Fixtures/EngineVersions.json` не
    тронут.

## 5. Продовые имена

- [x] 5.1 `docker-compose.prod.yml`, `deploy/deploy.sh`, `deploy/deploy.test.sh` и `.github/workflows/deploy.yml` — по таблице
  design.md, решение 8; раскладка служб и `DEPLOY_PROTOCOL` прежние. `docker-compose.yml` для разработки — только
  комментарий (решение 9). Проверить:
  - `shellcheck deploy/*.sh` и `bash deploy/deploy.test.sh` проходят;
  - `PROFIT_FOX_VERSION=1 PROFIT_FOX_DB_PASSWORD=x docker compose -f docker-compose.prod.yml config` завершается успешно и
    показывает образы `profit-fox:1` и `profit-fox-worker:1`;
  - строка `profit-fox-bench` остаётся только в `docker-compose.yml` и в комментарии о стенде Мака.
- [x] 5.2 Раздел «Production» и «Разовые шаги владельца» в `README.md` — новые имена образов, каталога, ключа, переменных.
  Формулировки открытых задач 8.4 и 8.5 в `openspec/changes/add-bench-service/tasks.md` — те же новые имена, отметки задач
  не менять. Проверить: в обоих файлах нет имён из левого столбца таблицы решения 8, кроме стенда Мака.

## 6. Итоговая проверка

- [x] 6.1 Поиск по репозиторию, кроме `openspec/changes` и каталогов сборки: строк `ProfitFox.Bench`, `ProfitFox.Engine`,
  `ProfitFox.Bot`, `src/bench`, `src/engine`, `src/bot` нет. Проверить: поиск пуст.
- [x] 6.2 Результаты расчёта прежние: `EngineVersion.Number` равен 2, `EngineVersions.json` совпадает с версией из
  `release/0.1.0` байт в байт, `EngineVersionTests` и `H0001ParityTests` зелёные.
- [x] 6.3 API прежнее: запустить API и выполнить `npm --prefix frontend run generate:api`. Проверить: `schema.d.ts` не
  изменился (контрольная сумма из раздела «База»); `npm --prefix frontend run typecheck`, `lint` и `test` зелёные.
- [x] 6.4 `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx` из `backend/`. Проверить:
  всё зелёное, число тестов равно базе, предупреждений сборки не больше базы; проверка однострочных `<summary>` из
  `ci.yml` проходит.

## Workflow follow-up

- Пул-реквест в `release/0.1.0` и архив изменения — по слову владельца, после ревью.
- До первого деплоя владелец говорит, какие разовые шаги уже сделаны под именами `profit-fox-bench`: их нужно повторить
  под новыми.
- Отдельные изменения позже: выбор машины для API и базы с выносом Worker'ов; переименование типов и ключей настроек с
  `Bench` в имени.

## База

Снято 2026-11-21 на коммите `9ec96fd` (`release/0.1.0` на `8a8b748` плюс предложение этого изменения), до правок. Среда —
облачный контейнер Linux, .NET SDK 10.0.115, Docker 29.10.0; не Мак владельца.

- `git merge-base --is-ancestor d8b2e08 HEAD` — успех.
- `dotnet test ProfitFox.slnx` из `backend/`: unit — 251 прошёл из 251, интеграционные — 162 прошли из 162, упавших и
  пропущенных нет.
- `dotnet build ProfitFox.slnx --configuration Release`: 2 предупреждения, 0 ошибок. Оба предупреждения — `NU1903` о пакете
  `Microsoft.OpenApi` 2.0.0 в шаблоне `ProfitFox.Bot.Presentation`; в остальных проектах предупреждений нет.
- Проверка однострочных `<summary>` из `ci.yml`: совпадений нет.
- SHA-256 файла `frontend/src/api/generated/schema.d.ts`:
  `56f993fcfd20a65f52df17cb56bfe0460173333c55cfba7913250153a631a3c5`. `npm --prefix frontend run generate:api` на прежней
  сборке даёт тот же файл.
- База разработки: `docker compose up -d --wait bench-db`, затем `dotnet run --project src/bench/ProfitFox.Bench.Presentation`.
  `/health` ответил `200 Healthy`. Таблица версий после запуска прежней сборки — одна строка:
  `202611200001 | 2026-11-21 05:13:06.270258 | CreateBenchTablesMigration`. Это база контейнера, созданная пустой в этой
  сессии, а не база Мака с журналом прогонов.

## Открыто после применения

Все задачи групп 1–6 отмечены: проверка каждой прошла в облачном контейнере. Итог против раздела «База»: unit — 251,
интеграционные — 162, предупреждений сборки 0 (было 2, оба ушли с шаблоном бота), контрольная сумма `schema.d.ts` та же.
Ниже — то, что осталось за владельцем, и решения, принятые при применении там, где tasks.md и design.md молчали.

Осталось проверить на машинах владельца:

- **Стенд на Маке (решения 7 и 9).** Задачи 1.1 и 2.3 выполнены на базе контейнера: она создана пустой в этой сессии,
  мигрирована прежней сборкой, и новая сборка оставила её таблицу версий как была. Базы Мака с журналом прогонов в
  контейнере нет. На Маке остаётся один запуск: `docker compose up -d --wait bench-db`,
  `dotnet run --project backend/src/ProfitFox.Presentation`, `/health` отвечает успехом, в таблице `VersionInfo` та же одна
  строка `202611200001`.
- **Сборка образов без подмен.** Обычный `docker build` в контейнере падает на `dotnet restore`: исходящий трафик идёт через
  прокси сессии, который переподписывает TLS, а образы `mcr.microsoft.com/dotnet/sdk` и `node` его сертификату не доверяют.
  Оба `Dockerfile` собраны без правок самих файлов: `docker build` с `--network host`, `--build-arg HTTPS_PROXY=…` и
  `--build-context <базовый образ>=docker-image://<тот же образ плюс сертификат прокси>` для четырёх базовых образов. Образы
  собрались, `ProfitFox.Presentation.dll`, `wwwroot/index.html` и `ProfitFox.Worker.dll` на месте, а `docker-compose.prod.yml`
  на этих образах под локальными метками `profit-fox:1` и `profit-fox-worker:1` поднял `db`, `api`, `worker-1`, `worker-2`;
  `/health` и страница ответили `200`. Первая сборка без подмен — в `deploy.yml` после push в `release/0.1.0`.

Решения при применении:

- **Строка `profit-fox-bench` вне деплоя (проверка задачи 5.1).** В `docker-compose.prod.yml`, `deploy/*.sh`, `deploy.yml` и
  `README.md` её нет; в `docker-compose.yml` она только в именах стенда Мака и в комментарии о них. Ещё пять вхождений
  оставлены намеренно: комментарии тестов `H0001ParityTests`, `OutcomeParityTests`, `VerdictRulesTests` и
  `bench/parity/README.md` называют так репозиторий прежнего Python-стенда (коммит `13bd168`), а не продовые имена из
  решения 8. Проверка 5.1 прочитана как проверка файлов деплоя. Если владелец читает её по всему репозиторию, эти пять
  мест — отдельное решение: комментарии тестов и каталог `bench/` это изменение не правит.
- **`BenchDatabase.cs` лежит в `Tests.Integration/Database/`, а его пространство имён — `ProfitFox.Tests.Integration`.** Это
  `[SetUpFixture]` NUnit, который поднимает один контейнер Postgres на всю сборку тестов; он действует на своё
  пространство имён и вложенные. С пространством имён `…Integration.Database` 99 интеграционных тестов из 162 падали без
  базы. Прежнее пространство имён `…Integration.Bench` потеряло уровень `Bench`, как и остальные. Другой выход — оставить
  файл в корне проекта тестов, но таблица решения 5 называет папку `Database/`.
- **Папки эталонов, которых нет в таблице решения 5.** `Tests.Unit/Bench/Fixtures/Verdict` переехала в
  `Tests.Unit/Fixtures/Verdict` по строке `Bench/<папка>`; `Tests.Unit/Engine/Simulation/Fixtures/Outcomes` — в
  `Tests.Unit/Trades/Fixtures/Outcomes` вместе со своим тестом `OutcomeParityTests`. Помощник `Paths.cs` остался в
  `Tests.Unit/Trades`; `EntryPlanTests` и `PairPositionTests` в `Tests.Unit/Positions` получили `using` на него.
- **`CompiledNode.cs` и `PinDeclaration.cs`** в коммите группы 3 видны как удаление и добавление, а не переименование: в
  файлах три строки, и смена строки `namespace` опускает сходство ниже порога git. Остальной текст совпадает.
- **`README.md`.** Имена проектов в первом абзаце и пути в разделе «Настройки» приведены к новым вместе с командами
  раздела «Стенд на Маке» (задача 2.2): иначе поиск задачи 6.1 не пуст.
- **`add-bench-service`.** В `tasks.md` изменена одна строка задачи 8.4 (`~/profit-fox/.env.prod`); в формулировке 8.5 имён из
  таблицы решения 8 нет. `design.md` и `proposal.md` этого изменения по-прежнему называют `profit-fox-bench`: по условию
  они не правятся.
