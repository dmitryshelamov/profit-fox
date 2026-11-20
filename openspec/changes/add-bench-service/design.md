# Design

## Context

Мотивация — в proposal.md. Состояние на `release/0.1.0` (`286ff4a`):

- **Стенд в четырёх слоях** после `split-bench-layers`: Domain, Application, Infrastructure, а также Presentation и Cli.
  У Application восемь портов. Файловые адаптеры:
  - `HypothesisFiles` — `bench/hypotheses/<папка>/hypothesis.json`;
  - `LedgerFile` — `bench/ledger.jsonl`;
  - `ReportFiles` — `bench/reports/<id>/*.md`;
  - `SignalFiles` — `bench/signals`;
  - `ParityDumpFiles`, а также `GitRepository` для коммита и чистого дерева.
- **`HypothesisRunner.Run`** читает гипотезу из папки, проверяет чистое дерево и правила журнала, считает N в начале,
  проходит 828 пар по одной и в конце пишет строку и отчёт. Отмены, кроме `CancellationToken`, и хода прогона нет.
- **`Hypothesis`** несёт `Folder` и `Hash` (SHA-256 нормализованного текста). Зерно случайных входов выводится из `Hash`
  и пары.
- **`LedgerRules`** работает по `(id, hash)`: другой хэш — отказ, окончательность и отложенный период — по id.
- **Тесты:**
  - `ProfitFox.Tests.Integration/Bench`: фейковый gRPC-сервер сервиса данных, `HypothesisRunTests` на синтетической
    истории, `BenchPresentationServer` над временной папкой `Bench:Folder`;
  - `ProfitFox.Tests.Unit/Bench/Fixtures/Verdict` — фикстуры вердикта.
- **Чего нет:** базы, Worker'а, CI, Dockerfile'ов. `main` — первый коммит, работа и деплой идут в `release/0.1.0` (решение
  владельца 2026-11-20).
- **Образец** — `profit-fox-v2`:
  - `ProfitFox.Worker`;
  - `Application/Workers/LeaseKeeper`;
  - `AddWorkerLeasesMigration` (колонки `lease_owner`, `leased_until`, `lost_leases`);
  - `MaxLostLeases = 3`.
- **Образец пайплайна** — `profit-fox-market-data`: `.github/workflows/ci.yml`, `deploy.yml`, `deploy/deploy.sh`,
  `docker-compose.prod.yml`. Там Npgsql, Dapper, FluentMigrator, Testcontainers.PostgreSql.
- **market-data** на soyo-02 пускает `HistoryRead:MaxConcurrentReads` чтений одновременно на весь сервис (2 по
  умолчанию), настройка меняется без перезапуска.

## Goals / Non-Goals

**Goals:**

- Правила стенда в Domain не зависят от того, кто их вызывает: Worker, тест или импорт. Новое — только хранилище, очередь
  и аренда.
- Прогон на стенде soyo-02 даёт тот же хэш данных и те же числа, что `bench parity` на Маке из
  `h0001-parity-v14-report.md`, при той же истории.
- Стенд поднимается на Маке одной командой для базы и двумя `dotnet run`.

**Non-Goals:**

- Перенос расчёта прогона по кускам между Worker'ами, как бэктест в v2. Прогон целиком у одного Worker'а.
- Общая база нескольких стендов, доступ к базе стенда по сети, Worker на soyo-01.
- Резервные копии, авторизация, метрики в Prometheus.
- Изменение расчёта событий, исходов и вердикта. Номер версии движка начинается с 1 на текущем коде.

## Decisions

### 1. Процессы и проекты

```text
Bench.Domain ◄─ Bench.Application ◄─ Bench.Infrastructure
                      ▲                    ▲
        ┌─────────────┼────────────────────┤
Bench.Presentation (Web)          Bench.Worker (Worker SDK)

Bench.Cli (Exe) — только HttpClient к Presentation, без ссылок на проекты стенда
```

- **Presentation** при старте мигрирует базу (FluentMigrator), без переноса из git (решение 8). Он отдаёт REST,
  предпросмотр с кэшем 5 пар и, если есть `wwwroot`, собранный фронтенд (`UseStaticFiles` + `MapFallbackToFile`). Прогоны
  он не считает.
- **Worker** — новый проект по образцу `ProfitFox.Worker` v2: `Host.CreateApplicationBuilder`, по фоновой службе на вид
  работы. Сейчас служба одна, `RunBackgroundService`. Миграций Worker не делает: при старте он ждёт, пока версия схемы в
  `VersionInfo` не станет не ниже своей, опрашивая её раз в 5 с.
- **Cli** не ссылается на Application: он говорит с API через свои маленькие DTO. Это держит его тонким клиентом.
- Альтернатива — исполнитель прогонов в процессе API (`BackgroundService`). Отклонена: владелец хочет от 1 до N
  независимых Worker'ов, и деплой API не должен прерывать прогоны.

### 2. Таблицы

| Таблица | Ключ | Поля | Заметки |
|---|---|---|---|
| `hypothesis_versions` | `(hypothesis_id, version)` | `hash` (уникален в паре с id), `text`, `created_at` | только вставка; отдельной таблицы гипотез нет, гипотеза — это её версии |
| `signals` | `id uuid` | `name` (уникально), `description`, `layout jsonb`, `version` | строка меняется; удаление каскадом |
| `signal_versions` | `(signal_id, version)` | `graph jsonb`, `created_at` | только вставка |
| `runs` | `id` (32 hex, как `runId` журнала) | `hypothesis_id`, `version`, `period`, `kind` (`verdict` \| `parity`), `status` (`queued` \| `running` \| `finished`), `queued_at`, `started_at`, `finished_at`, `lease_owner`, `leased_until`, `lost_leases`, `cancel_requested`, `pairs_done`, `pairs_total` | очередь и ход |
| `ledger` | `run_id` | поля `LedgerEntry`, плюс `version` и `engine_version`; коммита нет | триггер `BEFORE UPDATE OR DELETE` бросает ошибку |
| `reports` | `run_id` → `ledger` | `text` | тот же триггер |

- **Уникальность.** Частичный уникальный индекс `runs (hypothesis_id, version, period, kind) WHERE status <> 'finished'`
  держит требование «Двойная постановка» и тогда, когда два запроса пришли одновременно.
- **Снятие ждущего прогона** удаляет строку `runs` (`DELETE … WHERE status = 'queued'`). Идущий прогон получает
  `cancel_requested = true`.
- **Почему отчёт в своей таблице.** Отчёт пишется в той же транзакции, что строка журнала, а список прогонов не тащит
  текст отчётов.
- **Почему версии гипотезы хранят текст, а не разобранные поля.** Хэш и зерно случайных входов считаются по тексту, как
  сейчас. Перенесённая H0001 должна дать тот же хэш `5591d11a…`, иначе её строка `parity` оторвётся от неё.

### 3. Взятие прогона и аренда

- **Время аренды** считается часами базы (`now()`), а не часами Worker'а. Тогда Worker'ы на разных машинах не спорят из-за
  расхождения часов. В этом отличие от v2, где был `TimeProvider` Worker'а.
- **Взятие** — один запрос:

  ```sql
  UPDATE runs SET status = 'running', lease_owner = @worker, leased_until = now() + @lease,
         lost_leases = lost_leases + CASE WHEN status = 'running' THEN 1 ELSE 0 END,
         started_at = now(), pairs_done = 0
  WHERE id = (SELECT id FROM runs
              WHERE status = 'queued' OR (status = 'running' AND leased_until < now())
              ORDER BY queued_at LIMIT 1 FOR UPDATE SKIP LOCKED)
  RETURNING *
  ```

  Если у взятого прогона `lost_leases` = 3, Worker сразу пишет строку `failed` «прерван 3 раза» и не считает его.
- **`LeaseKeeper`** переносится из v2 почти без изменений: длительность аренды 30 с, продление раз в 10 с, опрос раз в
  1 с (`Worker:LeaseDuration`, `Worker:LeaseRenewal`, `Worker:PollInterval`).
- **Продление** — `UPDATE runs SET leased_until = …, pairs_done = @done WHERE id = @id AND lease_owner = @worker
  RETURNING cancel_requested`:
  - нет строки — аренда потеряна, работа отменяется;
  - `cancel_requested` — Worker отменяет расчёт и пишет `cancelled`.

  Так отмена доходит не позже чем через 10 с плюс время текущего запроса истории, укладываясь в 15 с из спецификации.
- **Имя Worker'а** — `Worker:Name`, по умолчанию имя машины и номер процесса. Оно показывается в ходе прогона.
- **Строка журнала** любого конца (вердикт, `parity`, `failed`, `cancelled`) пишется одной транзакцией. Транзакция
  проверяет `lease_owner = @worker`, вставляет строку и отчёт и ставит `status = 'finished'`. Worker, потерявший аренду,
  строки не напишет: требование «Аренда прогона».

### 4. Вердикт под блокировкой

- `HypothesisRunner` делится на две части:
  - чтение и расчёт по парам — как сейчас, строки `EventRow` в памяти;
  - решение — через новый порт `IRunLedger.Decide(Func<IReadOnlyList<LedgerEntry>, Decision> decide)`.
- Адаптер берёт `pg_advisory_xact_lock(<константа стенда>)`, читает журнал и вызывает `decide`. Внутри:
  - снова `LedgerRules.CheckCanRun`;
  - `Attempts`, мощность и вердикт;
  - адаптер вставляет строку и отчёт в той же транзакции.
- **Одна блокировка на весь стенд, а не на период.** Вердикты редки, а расчёт под блокировкой — это средние за блоки,
  миллисекунды.
- **Сверочному прогону блокировка тоже нужна:** число попыток, по которому считается его мощность, должно быть
  согласовано с журналом.
- Альтернатива — запрещать два прогона с вердиктом в одном периоде одновременно. Отклонена: тогда второй Worker стоит без
  дела именно тогда, когда он нужен.

### 5. Версии гипотез и правила журнала

- **`HypothesisReader.Parse(text)`** без папки:
  - id берётся из поля `id` и проверяется на вид `H` + четыре цифры;
  - `Hypothesis.Folder` заменяется на `Version`;
  - `Hash` — как сейчас, SHA-256 текста с `\r\n` → `\n`.
- **`LedgerRules`** переходит с `(id, hash)` на `(id, version)`:
  - проверка «другой хэш» уходит: другой хэш — это другая версия;
  - окончательность и отложенный период — по версии;
  - `Attempts` считает разные пары `(id, version)` с окончательным статусом.
- **`POST /api/hypotheses`** принимает текст `hypothesis.json` телом `text/plain`, чтобы хэш считался по присланным
  байтам, а не по пересобранному JSON. Ответы:
  - `201` и номер новой версии;
  - `200` и номер существующей версии при том же хэше;
  - `422` с ошибками разбора.

### 6. Номер версии движка

- **`EngineVersion.Number`** — константа в `ProfitFox.Bench.Domain`, начинается с 1. Её пишут строка журнала и отчёт.
- **Эталонный тест** в `ProfitFox.Tests.Integration/Bench/Runs` прогоняет H0001 на синтетической истории фейкового
  сервиса данных, той же, что в `HypothesisRunTests`. Из событий, исходов случайных входов и событий, средних за блоки и
  чисел вердикта он считает SHA-256.
- **Файл `Fixtures/EngineVersions.json`** — список `{ version, digest }`. Тест падает в двух случаях:
  - дайджест текущего кода не равен дайджесту записи с `version == EngineVersion.Number`;
  - в списке два одинаковых дайджеста.

  Поэтому смена результатов требует новой записи и нового номера.
- Альтернатива — номер из хэша сборки движка. Отклонена: он менялся бы от правки комментария и ничего не говорил бы о
  результатах.

### 7. Сигналы в базе

- `SignalLibrary` и её правила (имя, проверка графа, занятое имя) не меняются, меняется адаптер `ISignalStore`.
- `Write` сравнивает граф с последней версией по содержанию: канонический JSON без учёта порядка ключей, как в
  требовании «Сигнал H0001 собирается на холсте».
  - Граф отличается — вставляется `signal_versions` с номером `version + 1`.
  - Граф тот же — меняются только `layout` и `description`.
- Переименование меняет `name` у той же строки `signals`, копия — новая строка с версией 1.
- API сигналов отдаёт `version`. Черновик в браузере ключуется именем, как сейчас.

### 8. Без переноса из git

- **Решение владельца 2026-11-20:** база стенда начинается пустой. Из `bench/` ничего не переносится, у каждой строки
  журнала есть прогон в `runs`, версия гипотезы и номер версии движка.
- **H0001** загружают как любую гипотезу: `bench hypotheses add bench/hypotheses/H0001-funding-short-4h/hypothesis.json`.
  Текст тот же, поэтому версия 1 получает тот же хэш `5591d11a…`.
- **Строка `f3659dd4`** остаётся только в `bench/ledger.jsonl`. Это сверка, а сверки в число попыток N не идут, так что
  N от её отсутствия не меняется.
- **Альтернатива** — перенос при первом старте с пустой базой. Отклонена: механизм ради одной гипотезы и одной сверки, и
  пропущенная папка тихо оставляла бы базу без истории навсегда.

### 9. REST

| Запрос | Ответ |
|---|---|
| `GET /api/hypotheses` | id, имя, версия, таймфрейм, граф; на каждый период и вид — `canRun` и причина отказа (решение 5, для кнопок) |
| `POST /api/hypotheses` (`text/plain`) | решение 5 |
| `POST /api/runs` `{ hypothesis, period, kind }` | `201` с прогоном; `409` с причиной и `runId` мешающего прогона или строки журнала |
| `GET /api/runs?active=true\|false&limit=` | ждущие и идущие в порядке очереди или законченные от новых |
| `GET /api/runs/{id}` | прогон, ход, строка журнала и отчёт, если есть |
| `POST /api/runs/{id}/cancel` | `204`; ждущий удаляется, идущий получает `cancel_requested`; законченный — `409` |

- Причины отказа — текст `LedgerException`, как в CLI сейчас.
- Контроллеры — как в `Bot.Presentation`. Типы фронтенда генерируются из OpenAPI, как сейчас.

### 10. CLI

- **Команды** — в спецификации `bench-run-queue` «Команды CLI». Командную строку разбирает System.CommandLine 2.0: команды,
  опции, проверки значений и справка `--help` — его, а не свой разбор.
- **Адрес** — `Bench:Url` в `appsettings.json` CLI или `--bench <адрес>`.
- **`--wait`** опрашивает `GET /api/runs/{id}` раз в 5 с и печатает ход, когда меняется число пар.
- **Коды выхода:**
  - 0 — прогон поставлен, или прогон с `--wait` закончился вердиктом или `parity`;
  - 1 — отказ, `failed`, `cancelled` или стенд не отвечает;
  - 2 — неверная командная строка.

### 11. Фронтенд

- В `SignalListPage` у строк «Гипотезы стенда» кнопка «Прогнать» с выпадающим периодом и кнопка «Сверка». Доступность и
  причины берутся из `GET /api/hypotheses`.
- Маршруты:
  - `/runs` — опрос раз в 3 с, пока есть ждущие или идущие (`refetchInterval` TanStack Query);
  - `/runs/$id` — отчёт markdown, отрисованный как текст.

  Вопрос «какая библиотека markdown» решается в задаче. Достаточно таблиц: отчёт состоит из заголовков, таблиц и
  цитаты.
- В редакторе сигнала рядом с именем показывается «версия N».

### 12. Локальный стенд и тесты

- **`docker-compose.yml`** в корне — только `bench-db` (Postgres 17, `127.0.0.1:5435`, том `profit-fox-bench-dev-db`). Порт 5433
  на Маке занят локальной базой market-data.
- **`appsettings.Development.json`** Presentation и Worker содержат строку подключения к нему. Пароль локальной базы — не
  секрет.
- **`.claude/launch.json`** получает `bench-db` (`docker compose up bench-db`) и `bench-worker` (`dotnet run` Worker'а).
  Второй Worker — тот же `dotnet run` во втором терминале с `Worker__Name=mac-2`.
- **Интеграционные тесты** поднимают Postgres через Testcontainers.PostgreSql, один контейнер на сборку тестов, и сбрасывают
  таблицы перед каждым тестом. Тесты аренды идут на двух экземплярах службы Worker'а в одном процессе.

### 13. Деплой на soyo-02

- **Образы** собирает `deploy.yml` на `ubuntu-latest`:
  - `ghcr.io/dmitryshelamov/profit-fox-bench:<N>` — Dockerfile Presentation: стадия node собирает `frontend/` в
    `wwwroot`;
  - `ghcr.io/dmitryshelamov/profit-fox-bench-worker:<N>`.
- **`ci.yml`** — `dotnet build`, `dotnet test`, `npm ci`, `lint`, `test`, `build` фронтенда, `shellcheck` и тест
  `deploy.sh`. Срабатывает на push в `release/0.1.0`, на pull request и вручную.
- **`deploy.yml`** — копия market-data с заменами:
  - `branches: [release/0.1.0]` и `head_branch == 'release/0.1.0'`;
  - два образа;
  - `ssh … soyo@192.168.8.213 deploy "$VERSION" 1` ключом `~/.ssh/profit-fox-bench-deploy`.
- **`deploy/deploy.sh`** — копия market-data с заменами:
  - `REPOSITORY=dmitryshelamov/profit-fox`, `ROOT=~/profit-fox-bench`;
  - проверка «коммит в ветке» — `compare/release%2F0.1.0...<commit>`;
  - проверка ревизии обоих образов;
  - порядок: `db` (`--wait`), затем `bench` и ожидание `/health` 120 с, затем `worker-1` и `worker-2`.
- **`docker-compose.prod.yml`:**
  - `db` — `postgres:17`, том `profit-fox-bench-db`, порт не открыт, `mem_limit` 1g;
  - `bench` — `:5240`, `mem_limit` 512m, healthcheck `/health`;
  - `worker-1` и `worker-2` — `mem_limit` 1g, `Worker__Name` `soyo-02-1` и `soyo-02-2`.
  - Адрес market-data — `192.168.8.213:5237` и `:5236`. Секрет один: `BENCH_DB_PASSWORD` в
    `~/profit-fox-bench/.env.prod`.
- Альтернатива — подключить стенд к сети docker проекта market-data. Отклонена: адрес по локальной сети тот же, что у
  стенда на Маке, и проекты не связаны друг с другом.

## Risks / Trade-offs

- [Журнал только в базе, копий нет: решение владельца] → `deploy.sh` никогда не делает `down -v`, том назван явно.
  Резервная копия — отдельное изменение.
- [Изменение большое: база, Worker, CLI, фронтенд и пайплайн] → задачи сгруппированы так, что после каждой группы сборка и
  тесты зелёные. Пайплайн — последняя группа, его можно отложить, не трогая остальное.
- [Два Worker'а и предпросмотр делят 2 чтения market-data] → шаг владельца: `MaxConcurrentReads` = 3. Без него
  предпросмотр ждёт свободного места, около секунды на запрос.
- [Дисциплина номера движка держится на одном эталоне] → дайджест берёт события, исходы и вердикт целиком. Правка,
  меняющая результат только на реальных данных, этим тестом не ловится. Это записано в комментарии теста.
- [Деплой с `release/0.1.0` без обязательного PR] → остаются Approve в `production`, проверка ветки в `deploy.sh` и
  проверка ревизии образа.
- [Часы базы для аренды: Worker с отставшими часами не мешает] → взамен все времена аренды берутся из базы, тесты
  аренды идут на настоящем Postgres.
- [Прогон, прерванный трижды деплоями подряд, станет `failed`] → это не попытка, его ставят заново.

## Migration Plan

1. Код и тесты по группам `tasks.md` в `release/0.1.0`. Коммиты — по слову владельца.
2. Локально: `docker compose up bench-db`, Presentation, Worker, загрузка H0001 через CLI, затем сверка H0001 из
   интерфейса. Её хэш данных сравнивается с v14 `04ca021b…` с поправкой на рост истории.
3. Шаги владельца по слову:
   - раннер `soyo-03-profit-fox-bench`;
   - окружение `production` (проверяющий — владелец, только `release/0.1.0`);
   - ключ деплоя с принудительной командой на soyo-02, `~/profit-fox-bench/.env.prod` и `deploy.sh`;
   - `MaxConcurrentReads` = 3.
4. Первый деплой `v1`: Approve, затем проверка `http://192.168.8.213:5240/` и сверка H0001 на soyo-02.

Откат — деплой прежнего `N`. Миграции идут только вперёд. До первой новой версии схемы откат кода безопасен, после неё
откат делается новой версией.
