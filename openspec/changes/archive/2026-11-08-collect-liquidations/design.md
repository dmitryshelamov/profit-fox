# Design

## Context

Мотивация — в proposal.md. Как устроено сейчас (`main` 0cc4e6c):

- `ScannerSession` при взятии аренды вызывает `SubscribeAsync`: один `IMarketStreams.SubscribeMinuteCandlesAsync` по всем
  парам `market.Listed()`, закрытые свечи идут в `Channel`, полоса `WriteCandlesAsync` пишет их пачками через
  `ICandleRepository.UpsertAsync`; `restored` запускает догрузку по REST. Список пар меняется в REST-полосе, после чего
  подписка делается заново.
- `BybitMarketStreams` держит свечи на отдельном `BybitSocketClient`: пачки по 10 топиков, `TopicsPerConnection` (100) на
  соединение; CryptoExchange.Net переподключается сам и даёт события `ConnectionLost` / `ConnectionRestored` у подписки.
  Bybit.Net 7.7.0 умеет `SubscribeToAllLiquidationUpdatesAsync(symbols, …)` (топик `allLiquidation`, Bybit шлёт события
  пачками раз в 500 мс на символ; старый топик `liquidation` объявлен устаревшим). Событие: `Symbol`, `Side`, `Price`,
  `Quantity`, `UpdateTime`; `Side` — сторона ордера ликвидации (`Sell` закрывал лонг, `Buy` — шорт).
- `open_interest` — hypertable без сжатия и удаления, `funding_rates` — обычная таблица; `MarketPointRepositoryQueries`
  делает upsert точек по `(category, symbol, time)`. Сжатие TimescaleDB в проекте не применялось; образ базы
  `timescale/timescaledb:2.30.2-pg17` его поддерживает.
- `IScannerJournal` — только лог (`LoggingScannerJournal`); логи контейнеров ротируются 10 МБ × 3, централизованного
  хранения логов нет. Метрики: `Meter("ProfitFox.Bybit")`, регистрация в `MetricsRegistrar.AddMeter`.
- `FakeBybitSocketServer` в интеграционных тестах отвечает на `subscribe` любого топика и умеет `PushAsync(topic, data)`
  и обрыв соединений; e2e-стаб `bybit-stub.ts` тоже принимает любые топики.

## Goals / Non-Goals

**Goals:**

- Полная последовательность событий ликвидаций по всем linear-парам с первого дня после выкатки, с известными дырками.
- Один путь данных: поток → канал → пачка → хранилище, как у свечей; падение записи не роняет сессию.
- Диск под контролем: сжатие старых кусков, без удаления.

**Non-Goals:**

- Любые расчёты по ликвидациям, колонки снимка рынка, признаки идей, API и UI.
- Докачка истории: у Bybit её нет.
- Ликвидации `spot` и `inverse`.
- Настройка порогов сжатия и удаления старого: константы миграции.

## Decisions

### 1. Событие и сторона позиции

`LiquidationEvent(DateTimeOffset Time, LiquidationSide Side, decimal Price, decimal Quantity)` в `ValueObjects/Market`,
`LiquidationSide { Long, Short }` в `Entities/Market/Enums` с кодами `long` / `short` в хранилище. Сторона — ликвидированная
позиция, а не сторона ордера: `Sell` → `Long`, `Buy` → `Short`. **Поправка 2026-11-07 после выкатки v37** (fix
`fix/liquidation-side`): по документации Bybit `S` в `allLiquidation` — уже сторона ликвидированной позиции («a Buy update
means that a long position has been liquidated»), так что верно `Buy` → `Long`, `Sell` → `Short`; первые ~80 строк прода
были записаны наоборот, миграция `202611080006` развернула их. Так хранится то, что нужно анализу (кого вынесло), и
читателю не нужно помнить соглашение Bybit. Время — `UpdateTime` события в миллисекундах, как даёт биржа. Цена и объём
— `decimal`.

Отвергнуто: хранить сторону ордера как есть (каждый запрос к данным переворачивал бы её).

### 2. Подписка на отдельном сокет-клиенте тем же способом, что свечи

`IMarketStreams.SubscribeLiquidationsAsync(instruments, Action<Instrument, LiquidationEvent> liquidated, Action<DateTimeOffset>
lost, Action<DateTimeOffset> restored, ct)`: `BybitMarketStreams` делает второй `BybitSocketClient` с тем же
`SocketIndividualSubscriptionCombineTarget`, пачки по 10 символов через `V5LinearApi.SubscribeToAllLiquidationUpdatesAsync`,
отказ биржи → `ExchangeUnavailableException`, как у свечей. 822 пары → около 9 соединений сверх ~13 свечных. `lost` и
`restored` идут от `ConnectionLost` / `ConnectionRestored` каждой подписки с текущим временем.

`ScannerSession.SubscribeAsync` подписывает ликвидации сразу после свечей по `market.Listed()` категории `linear` и
освобождает обе подписки вместе; если подписка на ликвидации отказала, только что сделанная подписка на свечи
снимается, и REST-полоса пересоздаёт обе. Пересоздание подписки при смене списка пар тоже считается разрывом (решение
4): между снятием старой и созданием ~83 новых пачек проходят секунды без событий. Отдельный канал `_liquidations` и
полоса `WriteLiquidationsAsync` пишут пачками через `ILiquidationRepository.AddAsync`; ошибка записи —
`journal.Failed("liquidations", …)`, пачка теряется (докачать её нечем), это записывается как разрыв (решение 4).

Одно соединение несёт около десяти подписок-пачек, и `ConnectionLost` / `ConnectionRestored` приходят от каждой;
`BybitMarketStreams` сводит их в один разрыв потока: `lost` по первой потерянной подписке, `restored`, когда вернулась
последняя (уточнено при реализации).

Отвергнуто: один клиент на свечи и ликвидации (разрыв одного уронил бы догрузку свечей и наоборот; раздельные клиенты
дают раздельные `restored`); подписка на `allLiquidation` без списка символов — у Bybit такого топика нет.

### 3. Хранение: hypertable с уникальностью события и сжатием

Миграция `202611080005`:

```sql
CREATE TABLE liquidations (
    category text NOT NULL, symbol text NOT NULL, "time" timestamptz NOT NULL,
    side text NOT NULL, price numeric NOT NULL, quantity numeric NOT NULL
);
SELECT create_hypertable('liquidations', by_range('time', INTERVAL '1 day'));
CREATE UNIQUE INDEX "UX_liquidations_event" ON liquidations (category, symbol, "time", side, price, quantity);
ALTER TABLE liquidations SET (timescaledb.compress, timescaledb.compress_segmentby = 'category, symbol',
    timescaledb.compress_orderby = '"time"');
SELECT add_compression_policy('liquidations', INTERVAL '7 days');
```

Запись — `INSERT … ON CONFLICT DO NOTHING` пачкой; репозиторий возвращает число вставленных строк по сторонам, чтобы
счётчик метрики рос только на новые события. Куски по дню, чтобы сжатие шло ежедневно и несжатыми оставались ровно 7
дней. Чтение — `ILiquidationRepository.GetAsync(instrument, from, to, limit)` oldest first, как у OI и funding: нужно
тестам и будущему анализу. Повтор события возможен только после переподключения, когда Bybit присылает последнюю пачку
снова; уникальный индекс делает это безвредным.

Отвергнуто: минутные агрегаты (владелец: теряют каскад внутри минуты); без сжатия (до ~30 ГБ в год); удаление старого
(история — цель изменения).

### 4. Разрывы потока — таблица, не лог

`stream_breaks (stream text, lost_at timestamptz, restored_at timestamptz, PRIMARY KEY (stream, lost_at))`,
`StreamBreak(string Stream, DateTimeOffset LostAt, DateTimeOffset RestoredAt)`, `IStreamBreakRepository.AddAsync`.
Сессия запоминает `lost_at` из события `lost` (первое незаписанное), при `restored` пишет строку. Смена держателя:
новый держатель при подписке пишет разрыв от времени последнего события в `liquidations`
(`ILiquidationRepository.LastTimeAsync()`) до времени подписки; при первой подписке вообще, когда событий ещё нет, разрыв
не пишется. Ошибка записи пачки (решение 2) пишется как разрыв от времени первого до времени последнего события пачки.
Разрывы идут через свой канал и полосу `WriteBreaksAsync`, чтобы колбэки сокета не ждали базу; ошибка записи разрыва —
`journal.Failed("stream-breaks", …)`.

Запись — `INSERT … ON CONFLICT (stream, lost_at) DO UPDATE SET restored_at = GREATEST(…)` (уточнено при реализации): два
перезапуска сканера без новых событий дают один и тот же `lost_at`, и без этого второй запуск падал бы на первичном
ключе; разрыв от уже записанного момента растёт до более позднего конца.

Отвергнуто: запись в лог сканера (логи ротируются и не запрашиваются); метрика переподключений (Prometheus не скажет,
какие именно минуты пустые).

### 5. Метрика

`Meter("ProfitFox.Scanner")`, `Counter<long> profitfox.scanner.liquidations` с меткой `side`, увеличивается на число
вставленных строк каждой стороны; `MetricsRegistrar` добавляет meter. Класс `Scanner/ScannerMetrics` в Application, как
`BybitRestMetricsHandler` держит свой счётчик в Infrastructure; выбор места — где считается число вставленных строк.

### 6. Настроек нет

Поток ликвидаций всегда включён вместе с потоком свечей: в e2e список пар короткий (3 пары), стаб принимает подписку, а
отдельный флаг никто бы не выключал. Если прод-объём окажется неприемлемым, агрегация — отдельное изменение.

## Risks / Trade-offs

- [Объём потока неизвестен до выкатки] → сжатие с первого дня, куски по дню; смотреть размер `liquidations` в первую
  неделю; агрегация отдельным изменением.
- [Первое сжатие в проекте: политика требует фоновые воркеры TimescaleDB] → интеграционный тест проверяет, что политика
  есть в `timescaledb_information.jobs`; образ в `docker-compose` тот же, что на проде.
- [Уникальный индекс на 6 колонок по hypertable с сжатием] → индекс нужен только несжатым кускам, конфликты проверяются
  там; на сжатых кусках вставок нет.
- [Канал без границ при шторме ликвидаций] → как у свечей; пачка пишется на каждое пробуждение, запись одного INSERT с
  тысячами строк — миллисекунды.
- [Ещё 9 соединений к Bybit с того же IP] → публичные соединения не лимитируются как REST; свечные 13 уже работают.
- [Разрыв записывается по времени события `lost` клиента, не по времени последнего события биржи] → дырка может быть на
  секунды шире, чем нужно; анализ, который отбрасывает минуты с разрывом, от этого только консервативнее.

## Migration Plan

1. Миграция `202611080005` создаёт `liquidations` (hypertable, уникальный индекс, сжатие, политика 7 дней) и
   `stream_breaks`; выполняет API, как все миграции.
2. Выкатка образов; сканер при следующем взятии аренды подписывает ликвидации и начинает писать.
3. Откат: прежний образ сканера просто не подписывается; таблицы остаются с данными.

## Open Questions

Нет.
