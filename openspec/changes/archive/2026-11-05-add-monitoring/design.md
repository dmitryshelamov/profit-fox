# Design

## Context

Мотивация — в proposal.md, требования — в `specs/service-metrics` и `specs/monitoring`.

Как сейчас устроен REST к Bybit (`InfrastructureRegistrar.ConfigureCommon`):

- `services.AddBybit(...)` регистрирует общий `IBybitRestClient`. Bybit.Net 7.7.0 делает это через `AddHttpClient` и
  `ConfigurePrimaryHttpMessageHandler`, то есть клиент строится фабрикой `IHttpClientFactory`. Через общий клиент ходят
  `BybitMarketDataClient` (история свечей, правила и каталог инструментов) и `BybitPublicDataClient` сканера (за
  `ScannerRestGate`).
- `BybitAccountClient`, `BybitLedger` и `BybitOrderGateway` — типизированные `HttpClient` (`AddHttpClient<TPort, TImpl>`).
  Через `BybitKeyClients.Create` они передают свой `HttpClient` в `BybitRestClient`, у которого ключ живёт ровно один вызов.
- Повторы делает Polly (`ExchangeResilienceExecutor`) над вызовами SDK, так что каждая попытка — это отдельный HTTP-запрос.
- Тесты подменяют Bybit на уровне HTTP (`FakeBybitHandler`, `StubHttpHandler`), а `OutboundHttpGuard` отправляет
  пропущенные запросы в мёртвый прокси.

Метрик в коде сейчас нет совсем: ни OpenTelemetry, ни `System.Diagnostics.Metrics`.

Состояние soyo-03 (`192.168.8.142`, 4 ядра, 14 GB) известно по записям, не по проверке. Там работает runner GitHub Actions,
который только деплоит по SSH. Стоит ли там Docker и закрыты ли порты файрволом, не проверялось.

## Goals / Non-Goals

**Goals:**

- Одна точка в коде, через которую проходит каждая HTTP-попытка к Bybit REST от любого клиента, и через неё — счётчик и
  остаток лимитов.
- Ни одного нового порта и HTTP-слушателя в worker и scanner, ни одного нового порта на soyo-01.
- Отказ мониторинга никак не влияет на торговлю.
- Стек мониторинга и дашборд описаны кодом в репозитории и поднимаются одной командой.

**Non-Goals:**

- Алерты и их доставка — следующее изменение.
- Точный пик в скользящих окнах Bybit (1 с и 5 с). Берём корзины экспорта по 5 с (решение 4 в грилинге).
- Ограничение запросов по метрикам. Метрики только наблюдают и ничего не тормозят.
- Подпись аккаунта у метрик остатка лимита.

## Decisions

### 1. OpenTelemetry metrics и push по OTLP в Prometheus 3

Сервисы описывают метрики через `System.Diagnostics.Metrics` (`IMeterFactory`), а OpenTelemetry SDK отправляет их
`PeriodicExportingMetricReader` по OTLP (`http/protobuf`) на `Metrics:OtlpEndpoint` раз в `Metrics:ExportInterval` (5 с).
Используется кумулятивная temporality, её Prometheus принимает без флагов. Приёмник — встроенный OTLP-приёмник Prometheus 3:
флаг `--web.enable-otlp-receiver`, путь `/api/v1/otlp/v1/metrics`.

Пакеты: `OpenTelemetry.Extensions.Hosting` и `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Версии — последние стабильные
на момент apply.

Альтернативы, отклонённые на грилинге:

- Scrape `/metrics`. Worker и scanner потребовали бы HTTP-слушатель (`OpenTelemetry.Exporter.Prometheus.HttpListener`
  до сих пор beta, либо `prometheus-net`) и опубликованные порты на soyo-01.
- OpenTelemetry Collector между сервисами и Prometheus. Это лишний контейнер, пока у нас один приёмник.

### 2. Точка измерения — `DelegatingHandler` на всех клиентах фабрики, с фильтром по хосту Bybit

`BybitRestMetricsHandler` добавляется ко всем клиентам `IHttpClientFactory` через
`services.ConfigureHttpClientDefaults(builder => builder.AddHttpMessageHandler<...>())`. Учитываются только запросы, у
которых схема, хост и порт совпадают с адресом REST Bybit из настроек: `BybitSettings.RestAddress`, а если он не задан — с
адресом окружения `BybitSettings.Environment()`.

Обработчик стоит перед primary handler'ом, поэтому видит каждую попытку, включая повторы Polly. Тестовые подмены
`FakeBybitHandler` и `StubHttpHandler` заменяют только primary handler, так что обработчик проверяется и в интеграционных
тестах.

Уточнение на apply: в большинстве интеграционных тестов `ProfitFoxFactory` и `WorkerHost` подменяют общий
`IBybitRestClient` целиком (`new BybitRestClient(new HttpClient(FakeBybitHandler))`), мимо фабрики, поэтому его запросы там
не считаются. Обработчик на общем клиенте проверяет `BybitRestMetricsTests`: `FakeBybit = false` и `Bybit:RestAddress` на
локальный `HttpListener`, как в `BybitAddressTests`. Клиенты с ключом счёта в тестах заменяют только primary handler и
считаются как в проде.

Почему фильтр по хосту, а не по имени клиента. Имя типизированного клиента у Bybit.Net — деталь его реализации. Экспортёр
OTLP тоже может взять клиента из фабрики (`OtlpMetricExporter`): без фильтра он попал бы в счётчик запросов к Bybit.

Альтернативы:

- Встроенный meter `System.Net.Http` (`http.client.request.duration`). Он даёт число запросов по хосту, но без пути
  эндпоинта (`url.path` по умолчанию не пишется) и без `retCode`. Нужную нам разбивку он не даёт.
- Считать в `BybitKeyClients.Unwrap` и `RequestPageAsync`. Это четыре места вместо одного, повторы видны не везде, и новый
  клиент легко забыть.
- `AddHttpMessageHandler` для каждого клиента по имени. Привязывает код к внутреннему имени клиента Bybit.Net.

### 3. Подписи и исход

- `bybit.endpoint` — `request.RequestUri.AbsolutePath`. Пути v5 не содержат идентификаторов, поэтому кардинальность
  ограничена числом эндпоинтов, которые мы вызываем (меньше 20).
- `http.response.status_code` — код ответа. Если ответа нет (исключение во внутреннем обработчике), подписи нет. Обработчик
  считает попытку и пробрасывает исключение дальше без изменений.
- `bybit.outcome`:
  - `rate_limited` — HTTP 403, HTTP 429 или `retCode 10006`;
  - `error` — остальные не 2xx, ненулевой `retCode`, нет ответа;
  - `ok` — всё прочее.

`retCode` берётся из тела ответа 2xx: обработчик вызывает `LoadIntoBufferAsync` и читает верхнее поле `retCode` через
`Utf8JsonReader`, после чего SDK читает тело из буфера как обычно. Если тело не JSON или поля нет, исход `ok` по HTTP-коду.
Самый большой ответ, который мы получаем, — страница из 1000 свечей, порядка сотен килобайт. SDK всё равно читает его
целиком.

Уточнение на apply: тело читается через `ReadAsByteArrayAsync`, а не `LoadIntoBufferAsync` и `ReadAsStreamAsync`.
`HttpContent` кэширует поток `ReadAsStreamAsync`, и SDK, который читает тело тем же методом, получил бы поток, уже
прочитанный до конца, то есть пустое тело. `ReadAsByteArrayAsync` буферизует тело и отдаёт копию, а поток для SDK
остаётся нетронутым (тест `Body_is_read_whole_after_the_handler` на неперематываемом потоке). `Utf8JsonReader` останавливается
на `retCode`, который Bybit пишет первым полем, так что страница свечей не разбирается. Если тело — JSON, но не объект,
исход тоже `ok`: исключение из обработчика ушло бы в вызов SDK, а отказ мониторинга не должен влиять на торговлю.

### 4. Остаток лимита — observable gauges со сбросом минимума на сборе

Класс `BybitLimitHeadroom` хранит по эндпоинту последний `X-Bapi-Limit` и наименьший `X-Bapi-Limit-Status` с момента
прошлого сбора. Два `ObservableGauge` отдают его значения: `profitfox.bybit.rest.limit` — всегда после первого увиденного
заголовка, `profitfox.bybit.rest.limit_remaining` — только если за период были ответы. Callback остатка забирает минимум и
сбрасывает его. `PeriodicExportingMetricReader` вызывает callbacks один раз на экспорт, поэтому «период экспорта» из спеки и
есть период между сборами.

Подписи аккаунта нет. Обработчик не знает аккаунта, а единственное, что связывает запрос с аккаунтом, — заголовок
`X-BAPI-API-KEY`, который в метрики MUST NOT попадать. Минимум по всем аккаунтам — это запас самого загруженного
аккаунта, то есть консервативная оценка, а большего первое изменение не требует.

Альтернатива — синхронный `Gauge<T>` (последнее значение). Отклонена: последний остаток прячет провал внутри периода.

Уточнение на apply: `BybitLimitHeadroom` — singleton, gauges создаются в его конструкторе на meter `ProfitFox.Bybit`.
Обработчик (transient, по одному на цепочку клиента) только передаёт ему эндпоинт и заголовки ответа, а разбор
`X-Bapi-Limit` и `X-Bapi-Limit-Status` живёт в `BybitLimitHeadroom`. Ответ без одного из заголовков или с нечисловым
значением ничего не меняет: исключение здесь тоже ушло бы в вызов SDK.

### 5. Регистрация и настройки

- `Infrastructure/Observability/MetricsSettings` с полями `OtlpEndpoint` (строка, полный URL пути метрик; пусто — нет
  экспорта) и `ExportInterval` (`00:00:05`). Секция `Metrics` читается в `InfrastructureRegistrar`. Значения по умолчанию
  заданы в записи, в `appsettings.json` ничего добавлять не нужно. Прод задаёт `Metrics__OtlpEndpoint` через окружение.
- `Infrastructure/Observability/MetricsRegistrar.Configure(configuration, services, serviceName)` вызывают `Configure`
  (`api`), `ConfigureWorker` (`worker`) и `ConfigureScanner` (`scanner`). Имя попадает в ресурс OpenTelemetry как
  `service.name`, а в Prometheus становится подписью `job`.
- `BybitRestMetricsHandler` и `BybitLimitHeadroom` живут в `Exchanges/Bybit`: это знание о Bybit (хост, `retCode`,
  заголовки). Meter `ProfitFox.Bybit` регистрируется всегда. Без `OtlpEndpoint` `MeterProvider` с экспортёром не
  регистрируется, и инструменты пишут вхолостую.
- Имена инструментов: `profitfox.bybit.rest.requests` (counter, `{request}`), `profitfox.bybit.rest.limit` и
  `profitfox.bybit.rest.limit_remaining` (gauges, `{request}`). Prometheus 3 переводит их в
  `profitfox_bybit_rest_requests_total`, `profitfox_bybit_rest_limit` и `profitfox_bybit_rest_limit_remaining`, а
  атрибуты — в `bybit_endpoint`, `http_response_status_code` и `bybit_outcome`.

### 6. Стек на soyo-03 — `deploy/monitoring/`

- `docker-compose.yml`:
  - `prometheus` (`prom/prometheus:v3.x`) с флагами `--web.enable-otlp-receiver` и
    `--storage.tsdb.retention.time=15d`, порт `9090`, том данных;
  - `grafana` (`grafana/grafana:12.x`), порт `3000`, том данных, `GF_SECURITY_ADMIN_PASSWORD` из `.env`,
    `GF_AUTH_ANONYMOUS_ENABLED=false`.

  Образы закреплены точными тегами на момент apply, как `timescaledb` в проде: `prom/prometheus:v3.15.1` и
  `grafana/grafana:12.4.14` (последний 12.x; Grafana 13.x уже вышла, но решение и задача фиксируют мажор 12).
- `prometheus.yml`: блок `otlp` (перенос `service.name` и `service.instance.id` в `job` и `instance` по умолчанию),
  `storage.tsdb.out_of_order_time_window: 30m` (OTLP от нескольких сервисов приходит не по порядку), без `scrape_configs`.
- `grafana/provisioning/datasources/prometheus.yml` — источник по `http://prometheus:9090`;
  `grafana/provisioning/dashboards/dashboards.yml` — провайдер файлов; `grafana/dashboards/bybit-rest.json` — дашборд.
- Дашборды Grafana монтируются в `/etc/grafana/dashboards` (только чтение), отдельно от тома `/var/lib/grafana`.
- `.env.example` без значений, сам `.env` (600) — только на soyo-03. Корневой `.gitignore` игнорирует `.env.*`, поэтому в нём
  добавлено узкое исключение `!deploy/monitoring/.env.example`. Порты открыты на интерфейсах soyo-03 в LAN, без TLS,
  OTLP принимается без авторизации.

### 7. Запросы дашборда «Bybit REST»

Шаг панелей — 5 с (min interval `5s`). Корзины по 5 с дают точки экспорта, поэтому берётся `irate` по двум последним
точкам, умноженный на 5:

- Нагрузка на IP: `sum(irate(profitfox_bybit_rest_requests_total[15s])) * 5`, пороги 300 и 600.
- По сервисам: `sum by (job) (irate(...[15s])) * 5`; по эндпоинтам: `sum by (bybit_endpoint) (irate(...[15s])) * 5`.
- Остаток: `min by (bybit_endpoint) (last_over_time(profitfox_bybit_rest_limit_remaining[10s]))` и его доля от
  `max by (bybit_endpoint) (profitfox_bybit_rest_limit)`. Для серий, пришедших по OTLP, Prometheus не ставит
  staleness-маркеры, поэтому `last_over_time` с коротким окном не даёт тихому эндпоинту показывать старый минимум ещё 5
  минут.
- Отказы: `sum by (job, http_response_status_code) (increase(profitfox_bybit_rest_requests_total{bybit_outcome="rate_limited"}[1m]))`.

### 8. Прод

Через x-anchor в `docker-compose.prod.yml` у `api`, `worker` и `scanner` появляется
`Metrics__OtlpEndpoint: http://192.168.8.142:9090/api/v1/otlp/v1/metrics`. Это не секрет. Файл меняется только с
отдельного разрешения владельца, и выкатывается это обычной версией через Approve.

### 9. Что делает владелец, а что агент

- **[владелец]** Всё, что требует `sudo` на soyo-03: установка Docker, если его нет, файрвол, группа `docker`.
- **[агент, с разрешения]** Проверка soyo-03 только на чтение; копирование `deploy/monitoring/` и `.env` на soyo-03,
  `docker compose up -d`; изменение `docker-compose.prod.yml`. Перед каждым действием агент называет команды и ждёт «да».

## Risks / Trade-offs

- [Корзины экспорта не совпадают с окном Bybit 5 с, и пик может разрезаться пополам] → Ориентируемся на линию 300 (половина
  лимита), а точные окна — отдельным изменением, если понадобятся.
- [Периоды экспорта трёх сервисов не выровнены, сумма `irate` — оценка, а не точный счёт] → Её достаточно, чтобы видеть
  приближение к линии. В Grafana видна и разбивка по сервисам.
- [Сброс минимума на сборе ломается, если появится второй читатель метрик (второй экспортёр, `MeterListener`)] →
  Комментарий в `BybitLimitHeadroom`. В тестах callback вызывается явно, второго читателя в приложении нет.
- [Буферизация тела ответа — лишняя копия до сотен килобайт на ответ] → На нашем трафике это пренебрежимо. Если станет
  заметно, можно читать `retCode` только у ответов не из `/v5/market/*`.
- [OTLP без авторизации: любой в LAN может записать ложные метрики] → Принято для локальной сети, как UI прода.
- [Prometheus недоступен] → Экспортёр работает в своём фоновом потоке с таймаутом, а данные за время простоя теряются.
  Торговля и запросы к Bybit от него не зависят (сценарий «Приёмник недоступен»).
- [Подпись `instance` меняется при каждом рестарте контейнера] → Серии растут медленно. Дашборд суммирует по `job`, а не по
  `instance`.

## Migration Plan

1. Слить PR. Код с пустым `Metrics:OtlpEndpoint` ведёт себя как прежде.
2. С разрешения владельца проверить soyo-03 только на чтение. Если Docker не установлен, его ставит владелец.
3. С разрешения скопировать `deploy/monitoring/` на soyo-03, создать там `.env` (600) и выполнить `docker compose up -d`.
   Проверить вход в Grafana и пустой дашборд.
4. С разрешения изменить `docker-compose.prod.yml` (решение 8) и выкатить новую версию через Approve. Проверить, что на
   дашборде есть все три `job`.

Откат: выкатить предыдущую версию (без адреса OTLP сервисы не отправляют метрики); на soyo-03 — `docker compose down`.

## Open Questions

- ~~Точные теги образов Prometheus и Grafana и версии пакетов OpenTelemetry — выбираются на apply.~~ Выбраны:
  `OpenTelemetry.Extensions.Hosting` и `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1,
  `Microsoft.Extensions.Diagnostics.Testing` 10.11.0, `prom/prometheus:v3.15.1`, `grafana/grafana:12.4.14`. Переход на
  Grafana 13 — на усмотрение владельца.
- Закрыты ли порты 3000 и 9090 файрволом soyo-03. Выясняется на шаге 2 миграции и меняет только шаг владельца.
