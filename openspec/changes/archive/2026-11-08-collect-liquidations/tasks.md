# Tasks

Новая логика идёт строго по TDD: красный тест, минимальный код, рефакторинг. Ссылки «решение N» — на design.md,
сценарии — на дельты спек этого изменения. Цена и объём — `decimal`, время — UTC.

## 1. Домен

- [x] 1.1 `LiquidationSide { Long, Short }` (`Entities/Market/Enums`) с кодами `long` / `short`, `LiquidationEvent(Time, Side,
  Price, Quantity)` и `StreamBreak(Stream, LostAt, RestoredAt)` (`ValueObjects/Market`), `ILiquidationRepository`
  (`AddAsync` → число вставленных по сторонам, `GetAsync`, `LastTimeAsync`) и `IStreamBreakRepository.AddAsync`
  (решения 1, 3, 4). Тест кодов стороны зелёный, `docs/conventions.md` описывает новые типы.

## 2. Хранение

- [x] 2.1 Миграция `202611080005`: `liquidations` как hypertable по дню с уникальным индексом события, сжатием по
  `category, symbol` и политикой 7 дней; `stream_breaks` (решения 3, 4). Интеграционный тест миграции видит hypertable,
  индекс и политику в `timescaledb_information.jobs`.
- [x] 2.2 `LiquidationRepository`: пачечный `INSERT … ON CONFLICT DO NOTHING` с числом вставленных по сторонам, `GetAsync`
  oldest first, `LastTimeAsync`. Интеграционные тесты: сценарии «Ликвидация пришла» (поля читаются теми же), «Повтор
  события» (одна строка, вставлено 0), событие старше 7 дней читается после `compress_chunk` куска.
- [x] 2.3 `StreamBreakRepository.AddAsync`; интеграционный тест читает записанный разрыв.

## 3. Поток Bybit

- [x] 3.1 `IMarketStreams.SubscribeLiquidationsAsync` (решение 2) и `BybitMarketStreams`: второй сокет-клиент, пачки по 10,
  `allLiquidation`, `Sell` → `Long`, `Buy` → `Short`, `UpdateTime` → время события, `lost` / `restored` из событий
  подписки. Интеграционные тесты на `FakeBybitSocketServer`: сценарии «Ликвидация пришла» и «Сторона позиции» по
  пушу топика `allLiquidation.SOLUSDT`; 25 пар дают 3 запроса подписки; отказ биржи — `ExchangeUnavailableException`;
  обрыв соединений даёт `lost`, затем `restored`.

## 4. Сессия сканера

- [x] 4.1 `ScannerSession`: подписка ликвидаций по `linear`-парам `market.Listed()` вместе со свечами, канал
  `_liquidations`, полоса `WriteLiquidationsAsync` пачками (решение 2). Unit-тесты сессии: события из потока попадают в
  репозиторий одной пачкой; `spot`-пары не подписываются; пересоздание подписки при новой паре (сценарий «Новая пара в
  списке»); ошибка записи зовёт `journal.Failed("liquidations", …)` и пишет разрыв от первого до последнего события
  пачки.
- [x] 4.2 Разрывы (решение 4): `lost` запоминается, `restored` пишет `StreamBreak("liquidations", …)` (сценарий
  «Соединение восстановилось»); при подписке нового держателя разрыв от `LastTimeAsync` до подписки (сценарий
  «Сканер сменил держателя»), без событий в хранилище разрыв не пишется. Unit-тесты сессии.

## 5. Метрика

- [x] 5.1 `ScannerMetrics` с `Counter<long> profitfox.scanner.liquidations` и меткой `side`, рост на число вставленных
  строк каждой стороны (решение 5); `MetricsRegistrar` добавляет `ProfitFox.Scanner`. Unit-тест с `MeterListener`:
  сценарии «Пачка событий записана» и «Повтор не считается».

## 6. Проверки

- [x] 6.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; фронт не менялся — его проверки не нужны.
- [x] 6.2 Локально на dev: сканер против e2e-стаба подписывается на `allLiquidation` по его парам без ошибок в журнале;
  `/control`-пуша ликвидаций у стаба нет, поэтому запись события проверяется интеграционными тестами, а не вручную.

## Workflow follow-up

- `/opsx:archive` перед pull request.
- Прод: после выкатки смотреть размер `liquidations` и число строк в первую неделю; если объём далеко выше оценки —
  отдельное изменение с агрегацией.
