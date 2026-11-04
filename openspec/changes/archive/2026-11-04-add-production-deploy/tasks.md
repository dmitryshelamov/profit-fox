# Tasks

Задачи с пометкой **[владелец]** выполняет владелец: в них нужен `sudo` или настройки GitHub. Задачи с пометкой
**[агент, с разрешения]** меняют машины soyo-01 и soyo-03. Перед ними агент пишет точные команды и ждёт отдельного «да»
владельца в чате, а после — сообщает, что сделано.

## 1. Образ API

- [x] 1.1 Добавить `backend/src/ProfitFox.Presentation/Dockerfile` по образцу Worker'а (контекст `backend/`, порт
  `8080`). Проверка: образ собирается (`docker build -f backend/src/ProfitFox.Presentation/Dockerfile backend`). Контейнер
  с локальной базой из `docker-compose.yml` применяет миграции и отвечает 200 на `/openapi/v1.json`.

## 2. Прод в compose

- [x] 2.1 Добавить `docker-compose.prod.yml` (решение 3 design): `db`, `api`, `worker`, `scanner`, `web`. Образы
  `ghcr.io/dmitryshelamov/profit-fox-*:${PROFITFOX_VERSION}` (`PROFITFOX_VERSION` — номер версии), секреты из `PROFITFOX_DB_PASSWORD` и
  `PROFITFOX_MASTER_KEY`, мастер-ключ только у `api` и `worker`, порт базы не публикуется. Проверка: `docker compose -f
  docker-compose.prod.yml config -q` с тестовыми значениями проходит, а в выводе `config` у `scanner` нет мастер-ключа.
- [x] 2.2 Проверить compose целиком локально: собрать 4 образа с тегом `local`, поднять проект с тестовым `.env` (не
  коммитить) и открыть `http://localhost/market`. Сканер с тестовым `.env` не запускать, чтобы не ходить в Bybit:
  `docker compose up db api worker web`. Worker стартует с `Trading__Enabled=true` на пустой базе, ботов нет, ордеров
  нет. Проверка: страница открывается, API отвечает через `web`. После проверки проект удалить вместе с томом.

## 3. Скрипт выкатки

- [x] 3.1 Написать тест `deploy/deploy.test.sh` на разбор команды. Принимается только `deploy <N> <протокол>`, где
  `N` — положительное целое без ведущих нулей. Отказ: пустая команда, другой глагол, `0`, `07`, `-1`, не-число, лишние
  аргументы, `;` и прочие метасимволы, чужая версия протокола. Проверка: тест красный, потому что функции ещё нет.
- [x] 3.2 Написать `deploy/deploy.sh` (решение 4 design):
  - разбор команды из 3.1;
  - коммит тега `v<N>` через `git/ref/tags/v<N>` и проверка, что он в `main`, через `compare/main...<commit>`:
    `behind` или `identical` — в `main`, `ahead` и `diverged` — отказ (тест на все четыре статуса);
  - скачивание compose коммита тега `v<N>`;
  - `pull` и сверка метки `org.opencontainers.image.revision` образов с коммитом тега;
  - порядок «стоп worker и scanner → db → api с ожиданием `/openapi/v1.json` до 120 с → worker, scanner, web»;
  - запись номера в `~/profit-fox/current`, чистка образов старше 7 дней.

  Проверка: `deploy/deploy.test.sh` зелёный, `shellcheck deploy/*.sh` чистый.
- [x] 3.3 В `ci.yml` добавить шаги `shellcheck deploy/*.sh`, `bash deploy/deploy.test.sh` и `docker compose -f
  docker-compose.prod.yml config -q` с тестовыми значениями. Проверка: шаги проходят в CI на PR.

## 4. Workflow сборки и выкатки

- [x] 4.1 Добавить `.github/workflows/deploy.yml` (решение 5 design):
  - триггеры `workflow_run` (CI, `main`, проверка `event == 'push'` и того, что репозиторий свой) и
    `workflow_dispatch(version)`;
  - джобы `checks` → `build` → `deploy`, права по джобам (`build`: `contents: write`, `packages: write`),
    очереди `deploy-build` и `deploy-production` на джобах;
  - версия протокола совпадает с `deploy.sh`.

  Проверка: `actionlint`, если он есть, или ревью по чек-листу решения 5. Ни одна джоба не идёт на
  `pull_request`. Раннеры джоб пересмотрены в 4.3.
- [x] 4.2 Выдача номера в `build` (решение 2):
  - существующий тег `v*` коммита берётся как есть, иначе создаётся `v<max+1>` (первый — `v1`);
  - push существующего тега роняет сборку;
  - образы `:<N>` с меткой `revision`, уже опубликованные не пересобираются;
  - номер передаётся в `deploy` как output;
  - `deploy` для `workflow_dispatch(version)` требует, чтобы образы `:<N>` были в GHCR.

  Проверка: перезапуск сборки того же коммита оставляет прежний номер, это видно в 6.3.
- [x] 4.3 Перенести сборку с soyo-03 в облако (решение 5, пересмотрено):
  - `build` идёт на `ubuntu-latest`;
  - новая джоба `images` на `ubuntu-latest` с `packages: read`: вход в GHCR через `GITHUB_TOKEN`, проверка, что 4
    образа `:<N>` опубликованы, номер из `build` или из входа `version` — output для `deploy`; идёт для обоих
    триггеров;
  - `deploy` на `[self-hosted, soyo-03]` берёт номер из `images` и выполняет только `ssh`, без `docker`.

  Проверка: `actionlint`, если он есть; на `[self-hosted, soyo-03]` идёт только `deploy`, и в ней нет вызовов
  `docker`.

## 5. Документация

- [x] 5.1 В `README.md` добавить раздел о проде и деплое:
  - адрес прода и состав сервисов;
  - номерные версии `v<N>` и как выкатить (Approve) и откатить (`workflow_dispatch` с номером);
  - формат `.env.prod`;
  - как перенести новый `deploy.sh` на сервер;
  - пошаговая настройка машин и GitHub из решения 7: команды для владельца и для агента.

  Переписать абзац «On production, Postgres listens on the LAN interface»: порт базы закрыт до Worker'а на другой
  машине. Проверка: шаги в README совпадают с тем, что выполнено в группе 6.
- [x] 5.2 Обновить `CONTEXT.md` и `docs/conventions.md`, если в них упоминается прод или деплой. Проверка: `grep -n -i
  "prod\|deploy" CONTEXT.md docs/conventions.md` не находит устаревших утверждений.
- [x] 5.3 В `README.md` отразить 4.3: образы собираются на облачном раннере, soyo-03 только выкатывает по SSH; убрать
  шаг `usermod -aG docker soyo` для soyo-03. Проверка: в разделе о проде нет утверждений, что soyo-03 собирает образы,
  и нет шага с группой `docker` для soyo-03.

## 6. Настройка машин и первая выкатка

- [x] 6.1 **[владелец]** soyo-03:
  - поставить GitHub Actions runner для `dmitryshelamov/profit-fox` с меткой `soyo-03` службой.

  GitHub:
  - создать Environment `production` (reviewer — владелец, ветки только `main`);
  - включить «Require approval for all external contributors»;
  - добавить правило защиты тегов `v*`: нельзя удалять и перезаписывать. `GITHUB_TOKEN` workflow должен иметь право их создавать.

  Проверка: runner в статусе Idle в Settings → Actions → Runners.
- [x] 6.2 **[агент, с разрешения]**
  - на soyo-03 создать ключ `~/.ssh/profit-fox-deploy` и записать `known_hosts` soyo-01;
  - на soyo-01 добавить строку ключа с `from=`, `command=` и запретами в `authorized_keys`, поставить
    `~/profit-fox/deploy.sh` и создать `~/profit-fox/.env.prod` (`600`), генерируя секреты на сервере и не выводя их.

  Проверка: `ssh -i ~/.ssh/profit-fox-deploy soyo@192.168.8.111` без команды и с чужой командой получает отказ.
  Обычный вход `soyo` по своему ключу работает.
- [ ] 6.3 Первая выкатка после мержа PR:
  - workflow ставит тег `v1` и собирает 4 образа `:1` на облачном раннере;
  - **[владелец]** делает пакеты GHCR публичными и жмёт Approve.

  Проверка: `~/profit-fox/current` равен `1`, все 5 сервисов работают, `http://192.168.8.111/market` открывается с Mac,
  а в логе Actions нет секретов. Повторная выкатка `1` через `workflow_dispatch` проходит. Выкатка несуществующего
  номера отклоняется, прод не меняется.

## Workflow follow-up

- Ревью диффа до архива.
- `/opsx:archive` до PR, чтобы `openspec/specs/production-deploy` попал в `main` вместе с кодом.
- PR `feat(deploy): add production deploy` со squash.
- Группа 6.3 выполняется после мержа. Изменение архивируется до PR, поэтому эти задачи отмечаются в архивной копии или
  в отчёте PR.
