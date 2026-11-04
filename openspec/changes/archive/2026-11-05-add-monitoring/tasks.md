# Tasks

Задачи с пометкой **[владелец]** выполняет владелец: в них нужен `sudo`. Задачи с пометкой **[агент, с разрешения]** меняют
soyo-03 или `docker-compose.prod.yml`. Перед ними агент пишет точные команды и ждёт отдельного «да» владельца в чате, а
после сообщает, что сделано. Новая расчётная логика (решения 3 и 4 design) пишется строго red → green → refactor.

## 1. Зависимости

- [x] 1.1 Добавить в `backend/Directory.Packages.props` последние стабильные `OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` и `Microsoft.Extensions.Diagnostics.Testing` (версия 10.x), подключить первые
  два к `ProfitFox.Infrastructure`, третий — к `ProfitFox.Tests.Unit` и `ProfitFox.Tests.Integration`. Проверка:
  `dotnet build backend/ProfitFox.sln` проходит.

## 2. Счётчик запросов к Bybit REST

- [x] 2.1 TDD `BybitRestMetricsHandler` в `Exchanges/Bybit` (решения 2 и 3 design) на `StubHttpHandler` и
  `MetricCollector<long>`, по одному тесту за шаг. Сценарии из `specs/service-metrics`:
  - запрос свечей → `/v5/market/kline`, `200`, `ok`, путь без параметров;
  - 502 → `error`;
  - исключение внутреннего обработчика → `error` без кода, исключение проброшено;
  - 403 и 429 → `rate_limited`;
  - 200 с `retCode 10006` → `rate_limited`;
  - 200 с `retCode 170131` → `error`;
  - тело не JSON → `ok`;
  - запрос не на хост Bybit не считается;
  - тело ответа после обработчика читается целиком.

  Проверка: тесты в `Tests.Unit/Infrastructure/Exchanges/Bybit/BybitRestMetricsHandlerTests.cs` зелёные, и каждый был красным
  по ожидаемой причине.
- [x] 2.2 Подключить обработчик ко всем клиентам фабрики (`ConfigureHttpClientDefaults`) в
  `InfrastructureRegistrar.ConfigureCommon`, адрес Bybit взять из `BybitSettings`. Проверка: интеграционный тест через
  `ProfitFoxFactory` и `FakeBybitHandler`. Поиск инструментов (общий `IBybitRestClient`) и проверка ключа счёта
  (`BybitAccountClient`) увеличивают счётчик с нужными эндпоинтами. Существующая проверка, что все сервисы хостов
  разрешаются, остаётся зелёной.

## 3. Остаток лимита

- [x] 3.1 TDD `BybitLimitHeadroom` (решение 4 design), сценарии из `specs/service-metrics`:
  - наименьший остаток за период;
  - следующий период считается заново;
  - тихий период ничего не отдаёт;
  - лимит остаётся после тихого периода;
  - ответ без заголовков ничего не меняет.

  Проверка: тесты в `Tests.Unit/Infrastructure/Exchanges/Bybit/BybitLimitHeadroomTests.cs` зелёные, каждый был красным.
- [x] 3.2 Обработчик передаёт `X-Bapi-Limit` и `X-Bapi-Limit-Status` в `BybitLimitHeadroom`, gauges
  `profitfox.bybit.rest.limit` и `profitfox.bybit.rest.limit_remaining` регистрируются на meter `ProfitFox.Bybit`.
  Проверка: тест обработчика с `MetricCollector` для gauges и явным вызовом сбора (`RecordObservableInstruments`) видит
  лимит 10 и остаток 4 после ответов с остатками 9, 4, 7.

## 4. Экспорт по OTLP

- [x] 4.1 `Infrastructure/Observability/MetricsSettings` и `MetricsRegistrar` (решения 1 и 5 design). Вызвать их из
  `Configure`, `ConfigureWorker` и `ConfigureScanner` с именами `api`, `worker` и `scanner`. Без `OtlpEndpoint` экспортёр не
  регистрируется. Проверка: интеграционный тест с локальным HTTP-приёмником (`HttpListener` на свободном порту). Хост
  worker'а с `Metrics:OtlpEndpoint` на этот приёмник и запросом к фейковому Bybit за 2 периода экспорта (период в тесте —
  1 с) присылает POST `application/x-protobuf`, в байтах которого есть `profitfox.bybit.rest.requests` и `worker`. Без
  адреса за то же время на приёмник ничего не приходит. С адресом на закрытый порт запрос к фейковому Bybit проходит как
  обычно.
- [x] 4.2 Обновить `docs/conventions.md`: секция `Metrics` в «Settings» (поля, значения по умолчанию, `Metrics__OtlpEndpoint`
  на проде), `Observability` и метрики Bybit в «Infrastructure», правило «новый HTTP-клиент к Bybit — только через
  `IHttpClientFactory`, иначе его не видно в метриках». Проверка: в тексте названы все файлы, добавленные в группах 2–4.

## 5. Стек мониторинга в репозитории

- [x] 5.1 Добавить `deploy/monitoring/` (решение 6 design): `docker-compose.yml`, `prometheus.yml`, `.env.example`,
  provisioning источника данных и дашбордов. Образы — точные теги последних стабильных Prometheus 3 и Grafana 12.
  Проверка: `docker compose -f deploy/monitoring/docker-compose.yml config -q` с тестовым `.env` проходит, а
  `promtool check config` (из того же образа) принимает `prometheus.yml`.
- [x] 5.2 Дашборд `deploy/monitoring/grafana/dashboards/bybit-rest.json` с панелями и запросами решения 7 design:
  - нагрузка на IP с порогами 300 и 600;
  - по сервисам;
  - по эндпоинтам;
  - остаток и доля от лимита;
  - отказы `rate_limited`.

  Проверка — локально: поднять стек с тестовым `.env`, запустить API с `Metrics__OtlpEndpoint` на локальный Prometheus и
  `Bybit__RestAddress` на e2e-стаб (`frontend/e2e/bybit-stub.ts`), открыть поиск инструментов. Тогда в Grafana без ручной
  настройки появляется дашборд, на панелях нагрузки есть точки `job="api"`, вход без пароля невозможен. После проверки стек
  удалить вместе с томами, `.env` не коммитить.
- [x] 5.3 **[агент, с разрешения]** Добавить `Metrics__OtlpEndpoint` у `api`, `worker` и `scanner` в
  `docker-compose.prod.yml` (решение 8 design). Проверка: `docker compose -f docker-compose.prod.yml config` с тестовыми
  значениями показывает адрес у трёх сервисов и не показывает его у `db` и `web`.

## 6. Проверка изменения

- [x] 6.1 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` (ещё раз с `TZ=Asia/Tokyo`) и
  `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят. Пройти по diff: каждая ветка в новом коде требуется
  каким-то тестом.

## Workflow follow-up

- Ревью владельца, `/opsx:archive`, PR `feat(monitoring): add bybit rest metrics and monitoring stack` и squash-merge.
- **[агент, с разрешения]** Проверить soyo-03 только на чтение: `docker version`, `ss -tlnp`, `free -h`, `df -h`, `ufw status`
  без sudo, если возможно. **[владелец]** — поставить Docker или открыть порты в LAN, если нужно.
- **[агент, с разрешения]** Скопировать `deploy/monitoring/` из `main` на soyo-03, создать `.env` (600) с паролем, который
  владелец задаёт сам, выполнить `docker compose up -d`. Проверить вход в Grafana с машины LAN.
- Выкатить версию с этим изменением через Approve, когда стек на soyo-03 уже работает. Проверить на дашборде `job` `api`,
  `worker` и `scanner`.
