# Design

## Context

Мотивация — в proposal.md. Решения разбора 2026-11-08 (grill-me) о заявках на T-Invest записаны в design.md change
`add-moex-market` (решение 5) и здесь раскрываются в код. Как устроено сейчас (`main` 60d2ae3d):

- Живая торговля: `LiveBotSession` держит бота по аренде, берёт события из одной очереди (`CandleInput`, `OrderInput`,
  `FillInput`, `RestoreInput`, `StatusInput`, `WakeInput`, `ForeignFillInput`), пишет каждое изменение одной единицей работы и
  отправляет заявки после записи. `SendAsync` вызывает `IOrderGateway.PlaceAsync` / `CancelAsync`; `OrderSending.Resolve`
  превращает `OrderOutcomeKind` в шаг: `Transient` → `LiveOrder.Deferred(now)` с паузой `2^attempts` с, не больше минуты.
  `RebuildAsync` после старта, снятия с паузы и `RestoreInput` перечитывает заявки через `IExchangeLedger.FindOrderAsync` /
  `GetFillsAsync` и сверяет книги `LiveBotReconciler`'ом (`GetOpenOrdersAsync`, `GetHoldingAsync`). Обновление заявки
  (`OrderAsync`) берёт накопленные объём, стоимость и комиссию; заявка, ушедшая из `_orders`, обновлений больше не получает.
- Порты торговли: `IOrderGateway`, `IExchangeLedger`, `IExchangeStreams` и `IFormingCandleStreams` зарегистрированы одной
  реализацией Bybit (`BybitOrderGateway`, `BybitLedger`, `BybitStreams`); `ExchangeKey(ApiKey, Secret)` без биржи;
  `LiveRequests.KeyAsync` строит ключ из `account.ApiKey!`. Порты чтения уже маршрутизируются по `Category.Exchange()`
  (`Exchanges/Routing`), `ExchangeResilienceExecutor` — keyed по `Exchange`.
- `StartBotCommandHandler` отклоняет `moex` конфликтом до обращения к бирже (решение 10 change 1), для Bybit проверяет ключ
  через `IExchangeKeyInspector`, hedge-режим и деньги через `IExchangeLedger.GetFreeAsync(key, quote)`.
- `FormingCandleFeeds.JoinAsync` для инструмента T-Invest сразу отдаёт `NoData` (поправка ревью change 1).
- Адаптер T-Invest (`Exchanges/TInvest`): `TInvestCall` (лимитер, заголовки, дедлайн, метрики, перевод `RpcException`),
  `TInvestChannel` за `ITInvestCallInvokers`, `TInvestTokenSource` (токен самого старого аккаунта для чтений без счёта),
  `TInvestShares` (`TInvestShare(Uid, Ticker, Name, Lot, MinPriceIncrement, ShortEnabled)`), `TInvestCodes` (интервалы,
  окна), `TInvestAccountClient` (`IBrokerAccountReader`: `GetAccounts`, `GetPortfolio`). `MockTInvestApi` отвечает только на
  унарные вызовы. `TInvestMethods.GroupOf` уже относит `OrdersStreamService` и `MarketDataStreamService` к группам.
- Правила инструмента `moex`: `InstrumentRules(TickSize = шаг цены, QuantityStep = лот, MinQuantity = лот, MinAmount = 0)`,
  так что количество заявки в акциях всегда кратно лоту; `ListedInstrument.QuoteCoin = "RUB"`, `BaseCoin` = тикер.
- Пакет `Tinkoff.InvestApi` 0.6.25: `OrdersService.PostOrder` (`order_id` ≤ 36 символов — ключ идемпотентности,
  `quantity` в лотах, `time_in_force` только `DAY` / `FILL_AND_KILL` / `FILL_OR_KILL`), `CancelOrder` и `GetOrderState` по
  `OrderIdType.Request` или `Exchange`, `GetOrders` — активные заявки счёта; `OrdersStreamService.OrderStateStream`
  (серверный стрим, `order_request_id`, `lots_executed`, `trades`, `ping_delay_millis`), `TradesStream`;
  `MarketDataStreamService.MarketDataStream` (дуплекс, `SubscribeCandlesRequest` с `waiting_close`, `PingSettings`);
  `OperationsService.GetPositions` (`money`, `blocked`, `securities.balance / blocked`); `OrdersService.GetOrderPrice`;
  `InstrumentsService.TradingSchedules`; `MarketDataService.GetTradingStatus`, `GetOrderBook` (`limit_up`, `limit_down`).
- Опыт ProfitGraph (бот GAZP 2026-10-26..28, ADR 0023/0028/0031–0036/0039/0044/0050): стрим состояний объявляет заявку
  примерно за 200 мс до того, как `GetOrderState` знает её `order_request_id` (ответ `NOT_FOUND`, код `50005`);
  `GetOrderState` отвечает `NOT_FOUND` на любую заявку прошедшего дня; `TradingSchedules` отвечает только за сегодня и около
  недели вперёд; после закрытия сессии стрим состояний обрывался каждые 10–30 минут; коды отказов: `30034` нет денег,
  `30015/30016/30080` количество, `30017/30018/30100/30078` цена, `30052/30079` инструмент недоступен, `30042` риск-лимит,
  `30051` маржа, `30057` запрос уже известен, `30098` сессия закрыта, `30099` цена вне коридора; комиссия
  `executed_commission` при исполнении бывает `0` и появляется позже; ставка комиссии — `GetOrderPrice`
  (`deal_commission / initial_order_amount`), метода тарифа нет; стрим свечей без `waiting_close` не несёт признака закрытия.

## Goals / Non-Goals

**Goals:**

- Бот на `moex` торгует через ту же `LiveBotSession` без ветвлений по бирже внутри сессии: всё особенное у T-Invest живёт в
  адаптере и в маршрутизации портов.
- Ни одной заявки без записанного заранее идентификатора запроса; ни одной отправки в закрытую сессию.
- Остановка с понятной причиной везде, где состояние заявки неизвестно (ADR 0028/0034), вместо догадок.
- Проверяемость на `MockTInvestApi`, включая стримы, обрывы и ночь.

**Non-Goals:**

- Перенос остатка заявки через ночь, доказательства отсутствия по операциям, поздние комиссии дольше минуты.
- Стоп-заявки брокера, шорты, сделки по идеям на MOEX.
- Второй токен, Redis-лимитер, песочница.

## Decisions

### 1. Выключатель торговли на MOEX — `TInvest:Trading` на хосте API

`TInvestSettings.Trading` (`bool`, по умолчанию `false`) и зарегистрированный для API `Application/LiveTrading/
MoexTradingSettings(Enabled)`. `StartBotCommandHandler` первым делом отклоняет бота `moex` конфликтом «Trading on MOEX is
disabled», пока флаг выключен; Worker флага не читает — уже запущенного бота он ведёт. Так код этого change едет на прод с
выключенной торговлей, а решение разбора «боты на Moex без денег, пока владелец не скажет „включаем“» становится одной
переменной окружения `TInvest__Trading=true` (с одобрения владельца, после чек-листа), а не дисциплиной.

Отвергнуто: оставить конфликт в коде и снимать его отдельным релизом (лишний цикл ради одной строки); «бумажный режим» ботов
(у ботов его нет и не было; paper — режим авто идей, change `add-moex-ideas`). Это допущение агента: если владелец хочет
запускать без выключателя, решение вычёркивается вместе с задачей 2.4 и сценариями «Торговля на MOEX выключателем».

### 2. Биржа в `ExchangeKey` и маршрутизация портов торговли

`ExchangeKey(ApiKey, Secret, Exchange = Exchange.Bybit)`: для T-Invest `ApiKey` — номер брокерского счёта (не секрет),
`Secret` — токен. `LiveRequests.KeyAsync` строит ключ по `account.Exchange`. В `Exchanges/Routing` появляются
`RoutedOrderGateway`, `RoutedExchangeLedger`, `RoutedExchangeStreams` и `RoutedFormingCandleStreams`: первые три выбирают
реализацию по `key.Exchange` (`SubscribeAccountAsync` и `SubscribeCandlesAsync` — по ключу или по `Category.Exchange()`
инструмента), четвёртый — по категории; реализации Bybit и T-Invest зарегистрированы keyed по `Exchange`, как порты
чтения. Интеграционные тесты, которые подменяют порт целиком `FakeExchange`, не меняются.

Отвергнуто: маршрутизация только по инструменту (`SubscribeAccountAsync` инструмента не имеет); отдельные порты
`ITInvestOrderGateway` и ветвление в сессии (сессия не должна знать биржу).

### 3. Идентификаторы заявок: таблица `moex_orders`

`Infrastructure/Exchanges/TInvest/TInvestOrderIds` над таблицей `moex_orders(client_id text PK, account_id text NOT NULL,
request_id uuid NOT NULL UNIQUE, exchange_order_id text NULL, instrument_uid text NOT NULL, created_at timestamptz NOT
NULL)` (миграция `AddMoexOrdersMigration`). Перед первой отправкой заявки с данным `client_id` адаптер пишет строку со
случайным `request_id` (UUID v4, 36 символов — формат, который `PostOrder` принимает как `order_id`); повтор отправки читает
строку и шлёт тот же `request_id`. Ответ `PostOrder` дописывает `exchange_order_id`. Обратное отображение — по
`order_request_id` из `GetOrderState`, `GetOrders` и стрима: строки нет → заявка чужая (`ClientId = ""`). `30057` «запрос
уже известен» — принятие: адаптер читает состояние по `request_id` и отвечает `Accepted` с `exchange_order_id`.

Почему таблица, а не детерминированный UUID из `client_id` (ProfitGraph, UUIDv8 на попытку): обратное отображение
`order_request_id → client_id` нужно в любом случае (хэш не обратить), а попытка у нас одна — повтор идёт с тем же
идентификатором, идемпотентность на стороне биржи. Строка пишется вне единицы работы сессии: если процесс упал между записью
и отправкой, строка остаётся и следующая отправка её использует.

### 4. Заявка: лоты, `DAY`, без защиты

`TInvestOrderGateway.PlaceAsync`: `TInvestShares.ShareByAsync(symbol)` даёт `uid` и лот; `quantity / lot` должно быть
целым, иначе `Permanent` «not a whole number of lots» до отправки; `OrderType.Limit` → `ORDER_TYPE_LIMIT`, `Price` →
`Quotation`, `TimeInForce = DAY`; `OrderType.Market` → `ORDER_TYPE_MARKET` без цены и `time_in_force`; `Direction` по
стороне; `AccountId = key.ApiKey`. Заявка с `Protection`, `ReduceOnly` или `QuoteAmount` без количества — `Permanent`: в этом
change их не бывает (сессия бота шлёт рыночные заявки по количеству по последнему закрытию). `CancelAsync` — `CancelOrder`
по `OrderIdType.Request`; `CancelByExchangeIdAsync` — по `OrderIdType.Exchange`.

Истечение `DAY` в конце торгового дня — обычная отмена биржей: стрим приносит `CANCELLED`, `OrderAsync` даёт стратегии
`OrderCancelledByVenue`, `HoldLimit` выставляет заявку заново сразу, и отправка откладывается до открытия сессии (решение
6). Так решено на разборе; перенос остатка с частичным исполнением не делаем — остаток перевыставляется стратегией по её
правилам (решение A' change 5: заявка с исполнением перевыставляется только по цене).

### 5. Отказы биржи

`TInvestOrderGateway` переводит ответ в `OrderOutcome`:

| Ответ | Исход |
|---|---|
| успех `PostOrder` / `CancelOrder` | `Accepted` с `order_id` биржи |
| `30057` | `Accepted` (состояние прочитано по `request_id`) |
| `30098`, статус торгов «заявки не принимаются» | `Transient`, `RetryAfter` до открытия сессии (решение 6) |
| `30099`, цена вне коридора по стакану | `Transient`, `RetryAfter` минута |
| `30034`, `30015`, `30016`, `30080`, `30017`, `30018`, `30100`, `30078`, `30052`, `30079`, `30042`, `30051`, другой код `InvalidArgument` / `FailedPrecondition` / `OutOfRange` | `Permanent`, причина — «T-Invest refused the order: code N» |
| `UNAUTHENTICATED`, `PERMISSION_DENIED` | `Permanent` (как отозванный ключ на Bybit) |
| `UNAVAILABLE`, `INTERNAL`, `ABORTED`, `DEADLINE_EXCEEDED`, `RESOURCE_EXHAUSTED`, нет ответа | `Transient` |
| отмена: `NOT_FOUND` (`50005`), заявка уже в конечном состоянии | `AlreadyDone` |
| отмена: другой отказ | `Permanent` → `CancelRefused` сессии |

Текст брокера в причину не попадает (правило change 1); код — попадает. Неизвестный исход отправки (нет ответа) —
`Transient`: повтор с тем же `request_id` безопасен.

### 6. Проверка перед отправкой и пауза «не раньше чем»

`Infrastructure/Exchanges/TInvest/TInvestTradingStatuses` (singleton): `GetTradingStatus(uid)` и `GetOrderBook(uid, depth 1)`
кэшируются 5 с на инструмент; `TradingSchedules(exchange, сегодня..+7 дней)` — до конца дня. Заявка отправляется, только
если `api_trade_available_flag` и флаг её типа (`limit_order_available_flag` / `market_order_available_flag`) истинны, а
лимитная цена внутри `[limit_down, limit_up]` (пустой или перевёрнутый коридор — ждать). Иначе `Transient` без отправки.
`Exchange` акции берётся из `Share.Exchange` (поле добавляется в `TInvestShare`); начало следующей сессии — первый интервал
расписания биржевого типа после «сейчас» (словарь ProfitGraph: `opening_auction_morning`,
`regular_trading_session_morning|main|evening`, `closing_auction_main`; `dealer_*` не считаются); расписание не читается или
день неизвестен — минута.

`OrderOutcome` получает `RetryAfter` (`TimeSpan?`); `LiveOrder.Deferred(now, TimeSpan? notBefore = null)` берёт максимум
из нарастающей паузы и `notBefore`; `LiveBotSession.SendAsync` передаёт `outcome.RetryAfter`. `IdeaTradeOrder.Deferred`
получает тот же необязательный параметр без вызовов — сделки по идеям на T-Invest не ходят. Отмена отложенной заявки
(`RequestCancel`) сбрасывает `NextAttemptAt`, уходит сразу; заявки без `exchange_order_id` биржа не знает →
`RefreshAsync` закрывает её как отменённую — как сейчас.

Отвергнуто: прижимать цену к границе коридора (ProfitGraph) — меняет цену стратегии; повторять каждую минуту всю ночь (до 900
чтений статуса на бота за ночь, шум в метриках).

### 7. Состояние заявки, исполнения и комиссия

`TInvestOrderStates` строит `ExchangeOrder` из унарного `OrderState`: `ClientId` по `order_request_id` через таблицу;
`FilledQuantity = lots_executed × lot`; `FilledValue = Σ stages(price × quantity × lot)`; `Fee = executed_commission`,
`FeeCoin = "RUB"`; состояние: `NEW` / `PARTIALLYFILL` → `Open`, `FILL` → `Filled`, `CANCELLED` → `Cancelled`, `REJECTED` →
`Rejected` («rejected by the broker»); если сумма `stages` расходится с `lots_executed` — состояние перечитывается (две
части одного ответа, ProfitGraph). `ExchangeFill` — из `stages` (`trade_id`, `price`, `quantity × lot`, `execution_time`,
комиссия `0`): комиссию несёт заявка.

Сообщение стрима состояний — сигнал, не данные: адаптер читает `GetOrderState` по `request_id` до 5 раз с паузой в
секунду, пока биржа отвечает `NOT_FOUND` (ADR 0036), и отдаёт слушателям `ExchangeOrder` из унарного ответа и
`ExchangeFill` по новым `stages`. При `FILL` с `executed_commission = 0` адаптер перечитывает состояние через 5, 15, 30 и 60 с и
отдаёт конец заявки с первой ненулевой комиссией или через минуту с нулём: `OrderAsync` сессии не меняет законченную
заявку, так что комиссия должна прийти вместе с концом. Сообщение о заявке без строки в таблице — чужая заявка: при росте
`lots_executed` слушатели получают `ExchangeFill` с `ClientId = ""` и инструментом (`Instrument(Moex, ticker)`), что даёт
`ForeignFillInput` и сверку.

### 8. `TInvestLedger`

`GetFreeAsync(key, "RUB")` — `GetPositions(account)`: `money[rub]` минус `blocked[rub]`; `IsHedgeModeAsync` — `false`;
`GetHoldingAsync` — `securities` по `uid`: `balance + blocked` (акции; единицы — пункт чек-листа); `GetOpenOrdersAsync` —
`GetOrders(account)` по `instrument_uid`; `FindOrderAsync(clientId)` — строка таблицы → `GetOrderState` по `request_id`,
`NOT_FOUND` → `null` без повторов (прошедший день, ADR 0034); `FindOrderByExchangeIdAsync` — по `OrderIdType.Exchange`;
`GetOrdersOfParentAsync` — пусто; `GetFillsAsync` / `GetOrderFillsAsync` — `stages` состояния; `GetFeesAsync` —
`GetOrderPrice(buy, lots на ≥ 100 000 RUB по `GetLastPrices`)`, ставка `deal_commission / initial_order_amount` для лимитной и
рыночной, `FeeSchedule(rate, rate, Moex)` (`AssetOf` → `Quote`, то есть рубли).

`FindOrderAsync → null` для активной заявки сессия не трогает (как сейчас), а сверка называет такую заявку расхождением и
останавливает ботов: после ночи без Worker'а с истёкшей заявкой бот встанет с причиной «order … is not on the exchange» —
это принято: исполнения прошедшего дня у биржи не прочитать (ADR 0034), а догадка стоила бы денег.

### 9. `TInvestStreams`: стримы на аккаунт и на процесс

Singleton `TInvestStreams : IExchangeStreams, IFormingCandleStreams` по образцу `BybitStreams` (топики со счётчиком
слушателей, первый открывает, последний закрывает):

- Заявки: `OrderStateStream(accounts = [key.ApiKey])` с токеном ключа, `ping_delay_millis = 30 000`; один стрим на аккаунт,
  сколько бы ботов его ни слушали. Тишина без пингов 90 с или конец стрима → переподключение с паузами 1, 2, 5, 10, затем
  30 с; после восстановления каждому слушателю `restored`. В конце торгового дня по расписанию (+1 мин) стрим тоже отдаёт
  `restored`: сессии перечитывают заявки и сверяются, так что истечение, которого стрим не объявил, остановит бота с причиной
  в ту же минуту, а не оставит «активную» заявку-призрак до рестарта.
- Свечи: два дуплексных `MarketDataStream` на процесс с токеном `TInvestTokenSource`: «закрытые» (`waiting_close = true`,
  `candle_source_type = EXCHANGE`) для `IExchangeStreams.SubscribeCandlesAsync` и «текущие» (`waiting_close = false`) для
  `IFormingCandleStreams`; подписка и отписка на серию — `SubscribeCandlesRequest` в тот же стрим под семафором;
  `PingSettings 30 с`; тишина 90 с без пингов → переподключение с повторной подпиской всех серий и `restored` слушателям. Свеча:
  `Candle(time, o, h, l, c, volume × lot, volume × lot × (h + l + c) / 3)` — как у истории change 1; лот — из `TInvestShares`
  при подписке.
- Открытие стрима считается запросом его группы в `TInvestMetrics` (`outcome = ok` / `error`), обрыв — `outcome = lost`.

Почему два стрима свечей, а не один с выводом закрытия по времени: закрытие объявляет биржа, незакрытая свеча не доходит до
стратегии по определению, а одновременная подписка одной серии с разным `waiting_close` в одном стриме не проверена. Лимиты
(300 подписок на стрим, 32 стрима на токен) далеко.

Отвергнуто: опрос `GetCandles` после закрытия (ProfitGraph T9b) — лишние запросы к общему лимиту `MarketData`; `TradesStream`
вместо `OrderStateStream` — исполнения и так приходят в `trades` состояния.

### 10. Запуск бота на MOEX

`StartBotCommandHandler`: выключатель (решение 1) → аккаунт → ключ → для `Exchange.TInvest` проверка счёта
`IBrokerAccountReader.ListAccountsAsync(token)` + `BrokerAccountRules.Problem` (не найден, не открыт, не полный доступ →
конфликт с причиной); hedge и `IExchangeKeyInspector` только для Bybit → вынос в приватные `CheckBybitKeyAsync` /
`CheckTInvestAccountAsync`; валюта котировки `RUB` из каталога как сейчас; деньги — `IExchangeLedger.GetFreeAsync(key,
"RUB")` через маршрутизатор. Конфликт «Trading on MOEX is not supported yet» уходит из кода и спеки.

### 11. `FormingCandleFeeds` и регистрация

`JoinAsync` перестаёт отвечать `NoData` для T-Invest: поток идёт через `RoutedFormingCandleStreams`. Регистрация в
`InfrastructureRegistrar`: `BybitStreams` и `TInvestStreams` keyed по `Exchange` для обоих интерфейсов стримов;
`BybitOrderGateway` / `TInvestOrderGateway`, `BybitLedger` / `TInvestLedger` keyed; маршрутизаторы — как единственные
реализации портов. `TInvestOrderIds` живёт в Infrastructure над Dapper, своей миграцией в общем списке.

### 12. Тесты и мок

`MockTInvestApi` учится стримам: `OnServerStream<TRequest, TResponse>(method, script)` и
`OnDuplex<TRequest, TResponse>(method, script)`, где скрипт — объект с `Channel` ответов, записью запросов (для дуплекса — и
входящих сообщений) и методами `Complete()` / `Fail(status)`, чтобы тест оборвал стрим. Unit-тесты по классу адаптера на
моке (Tests.Unit зеркалит `src`): гейтвей (лоты, `DAY`, таблица кодов, `30057`, проверка статуса и коридора, `RetryAfter` по
расписанию), леджер (деньги, позиция, заявки, комиссия пробой), состояния (маппинг, расхождение `stages`, комиссия позже),
статусы (кэш, следующая сессия, словарь интервалов), стримы (один на аккаунт, сигнал → перечтение с повторами, чужая
заявка, переподключение, тишина, конец дня, две подписки на серию, отписка). Application: `OrderSending` без изменений;
`LiveBotSession` c `RetryAfter` на `FakeExchange`; `StartBot` для `moex` (выключатель, доступ счёта, рубли);
`FormingCandleFeeds` для T-Invest. Integration: регистрация keyed портов и маршрутизаторов; сценарий Worker'а на моке
«бот через ночь»: закрытая свеча → дневная лимитка → `NEW` в стриме → `CANCELLED` в конце дня → перевыставление → статус
«закрыто» → пауза до открытия по расписанию → отправка утром; и «исполнение с поздней комиссией». Без e2e и без изменений
фронтенда.

### 13. Чек-лист владельца на реальном счёте (с разрешения, по шагам)

Только чтения до последнего пункта:

- `GetPositions`: единицы `securities.balance` (акции или лоты) и `blocked`; `money` / `blocked` по `rub`.
- `TradingSchedules(moex_mrng_evng_e_wknd_dlr)` на сегодня: типы интервалов и их времена — сверить словарь решения 6.
- `GetTradingStatus` и `GetOrderBook(depth 1)` `SBER` в сессию и ночью: флаги, `limit_up` / `limit_down`.
- `GetOrderPrice` лимитной покупки `SBER` на ≥ 100 000 ₽: `deal_commission / initial_order_amount`.
- `OrderStateStream` с пингами в сессию и ночью: как часто рвётся, приходят ли пинги.
- Первая реальная заявка — только по слову владельца: бот с минимальным лотом на ликвидной акции, лимитная покупка далеко от
  рынка → проверить `order_request_id` в стриме и `GetOrderState`, отмена → `CANCELLED`; затем повтор `PostOrder` с тем же
  `order_id` — та же заявка или `30057`; заявка, оставленная до конца дня — приходит ли `CANCELLED` в стриме и что отвечает
  `GetOrderState` на следующий день.

Результаты — поправкой в этот раздел; расхождения с решениями 5–9 правятся до включения `TInvest:Trading` на проде.

## Отклонения при внедрении

- **Задачи 3.2 и 3.3 выполнены после группы 8, а не в группе 3.** Маршрутизаторам портов торговли нужны keyed-реализации
  T-Invest (`TInvestOrderGateway`, `TInvestLedger`, `TInvestStreams`), которых до групп 6–8 нет: регистрация с недостающей
  реализацией не проходит проверку DI хостов (`ValidateOnBuild`), а `FormingCandleFeeds` без `NoData` до появления
  `TInvestStreams` снова отправил бы серию `moex` в стримы Bybit (ошибка, исправленная ревью change 1). В группе 3 сделаны
  маршрутизаторы (3.1) с unit-тестами; регистрация и снятие `NoData` — одним шагом, когда реализации T-Invest готовы.
- **Ошибки T-Invest несут код.** Отказ `TInvestCall` для статусов кроме транспортных — `TInvestRefusedException` с кодом из
  описания статуса (`TInvestNotFoundException` — его наследник), чтобы гейтвей разбирал таблицу отказов (решение 5) по коду, а
  не по тексту сообщения.
- **«Заявка уже в конечном состоянии» при отмене определяется чтением состояния, а не кодом.** Код такого отказа в решениях не
  записан; при постоянном отказе `CancelOrder` гейтвей читает `GetOrderState` и отвечает `AlreadyDone`, если заявка исполнена,
  отменена или отклонена (или биржа её не знает), иначе `Permanent` с кодом.
- **`30057`, а состояние по `request_id` ещё «не найдено»** — `Transient` без паузы биржи, а не `Permanent`: биржа объявляет
  запрос раньше, чем `GetOrderState` его знает (ADR 0036), а повтор идёт с тем же `request_id` и снова получит `30057`.
- **`ITInvestOrderIds`.** `TInvestOrderIds` стоит за внутренним интерфейсом, чтобы unit-тесты гейтвея, леджера и стримов на
  `MockTInvestApi` подменяли таблицу NSubstitute (Infrastructure открывает внутренние типы `DynamicProxyGenAssembly2`); сама
  таблица проверяется интеграционным `TInvestOrderIdsTests`.
- **`restored` в конце торгового дня приходит слушателям закрытых свечей серии, а не стрима заявок.** У стрима заявок нет
  инструмента, значит и расписания (`Share.Exchange`); у серии оно есть. Каждая сессия бота слушает закрытые свечи своей акции,
  и `restored` с любой подписки ведёт к одному и тому же `RebuildAsync` со сверкой, так что эффект решения 9 тот же: через минуту
  после конца дня по расписанию акции сессии перечитывают заявки. Расписание не читается — следующая попытка через час.
  Слушатели текущих свечей (страница бота) этого сигнала не получают.
- **Подписка на текущие свечи тоже идёт с `candle_source_type = EXCHANGE`**, как на закрытые: один построитель запроса на оба
  стрима, а источник свечей графика совпадает с историей.
- **Без `T-Invest`-аккаунта стрим свечей ждёт и открывается снова**: токен стримов свечей — `TInvestTokenSource`, его отсутствие —
  обычный обрыв с нарастающей паузой.
- **Сверка искала соседей бота `moex` среди ботов спота.** `LiveBotReconciler` считал держателями общей позиции ботов того же
  инструмента только на `linear`, а для любой другой категории — ботов `spot` той же базовой монеты (по каталогу спота Bybit), так
  что бот `moex` в сверку не попадал вовсе, а сверка ходила в Bybit. Найдено сценарием 10.3; теперь по монете — только спот, на
  `linear` и `moex` — по инструменту, для ботов и для сделок по идеям.
- **Тексты сессии бота без имени Bybit.** Причины «Bybit refused to cancel order … Cancel it on Bybit» и запасные «Bybit
  rejected/refused the order» стали бы неверны для бота `moex` и нарушали правило «Application не знает вендоров»: теперь «The
  exchange …»; так же журнал Worker'а («The exchange took order …»). Отказ запуска по каталогу называет биржу категории
  (`ExchangeCodes.DisplayName`: «T-Invest no longer lists SBER in moex.»). Тексты сделок по идеям не менялись: они только на Bybit.
- **Кэш расписания — до конца суток UTC**, а не московских: время в системе только UTC; расписание запрашивается с «сейчас» на
  7 дней вперёд, так что дата в прошлом (`30003`) не запрашивается, а отказ всё равно даёт «расписания нет» (минута).
- **Сообщение стрима заявок, по которому состояние не прочиталось, не теряется молча.** Чтение по сигналу (`GetOrderState`,
  таблица `moex_orders`, `ShareBy`) может упасть — отказ или недоступность биржи, ошибка БД; раньше исключение уходило в
  фоновую задачу, и бот узнавал об исполнении только при следующем `restored`. Теперь такое сообщение считается в метрике как
  `OrderStateStream` / `error`, а всем слушателям аккаунта приходит `restored`, и сессии перечитывают заявки через
  `RebuildAsync`; сам стрим продолжает работать. Уход последнего слушателя во время чтения ошибкой не считается.
- **Стрим заявок аккаунта переоткрывается с новым токеном.** Стрим кэшируется по номеру счёта, а токен брался из ключа первого
  подписчика и жил до перезапуска процесса: после смены токена в UI стрим вечно падал с `UNAUTHENTICATED`, а новые подписчики
  присоединялись к мёртвому стриму. Теперь подписчик с другим токеном того же счёта меняет ключ стрима и рвёт текущий вызов;
  цикл сразу, без паузы и без `lost` в метрике, открывает стрим с новым токеном и говорит слушателям `restored`; стрим в паузе
  возьмёт новый токен при следующем открытии.
- **Сессия, начавшаяся меньше минуты назад, — текущая, а не прошедшая.** `NextSessionStartAsync` брал только старты строго
  после «сейчас»: сессия бота просыпалась ровно в момент открытия, статус ещё отвечал «заявки не принимаются», и пауза ставилась
  до следующего интервала — утренняя сессия пропускалась целиком. Теперь старт в пределах последней минуты (`start > now − 1 мин`)
  возвращается как ближайший, а `TInvestOrderGateway.UntilSessionAsync` на старт, который уже наступил, отвечает `RetryAfter`
  минута, как без расписания; пауза до открытия ставится только на старт в будущем.
- **Любое исключение в цикле стрима — обрыв, а не конец стрима.** Циклы `OrderStateStream` и `MarketDataStream` ловили только
  `RpcException`, `TimeoutException` и `ExchangeException`; любое другое (свеча с непригодными данными в `Dispatch`, бросивший
  колбэк слушателя) завершало цикл навсегда, `_writer` указывал на мёртвый стрим, и новые подписки писали в него. Теперь, как в
  `BybitStreams`, ловится всё, кроме отмены: обрыв считается `lost` (или `error`, если стрим не успел открыться), стрим
  переоткрывается с паузой, серии подписываются заново, слушатели получают `restored`; следующее сообщение доходит.

## Итоги внедрения

Группы 1–10 `tasks.md` выполнены в ветке `feat/add-moex-live-bots` (12 коммитов поверх предложения e628b5e), группа 11
(чек-лист на реальном счёте и ручная проверка в dev) оставлена владельцу; `TInvest:Trading` по умолчанию выключен.

Что сделано:

- Домен и порты: `ExchangeKey.Exchange`, `OrderOutcome.RetryAfter`, `LiveOrder.Deferred` / `IdeaTradeOrder.Deferred` с паузой биржи,
  ключ T-Invest из номера счёта и токена (`LiveRequests.KeyAsync`).
- Запуск бота `moex`: выключатель `TInvest:Trading` → `MoexTradingSettings`, проверка счёта через `IBrokerAccountReader`, рубли
  через маршрутизатор леджера; конфликт «not supported yet» удалён.
- `Exchanges/Routing`: маршрутизаторы четырёх портов торговли — единственные регистрации, реализации Bybit и T-Invest keyed по
  `Exchange`; `FormingCandleFeeds` без `NoData` для T-Invest.
- `Exchanges/TInvest`: `moex_orders` (`AddMoexOrdersMigration`, `TInvestOrderIds`), `TInvestOrderStates`, `TInvestTradingStatuses`
  (статус, коридор, расписание, конец торгового дня), `TInvestOrderGateway` (лоты, `DAY`, таблица отказов по коду),
  `TInvestLedger`, `TInvestStreams` (стрим заявок на аккаунт, два стрима свечей на процесс, перечтение состояния, поздняя
  комиссия, переподключение, тишина, конец дня, метрики `ok` / `error` / `lost`); `MockTInvestApi` отвечает на серверные и
  дуплексные стримы.
- Сессия бота передаёт `RetryAfter` в отсрочку заявки; сценарии Worker'а на `FakeTInvest` поверх `MockTInvestApi`: «бот через
  ночь», «поздняя комиссия», «заявка бота неизвестна бирже».
- `docs/conventions.md`: запуск на MOEX, маршрутизация портов торговли, `FormingCandleFeeds`, адаптер T-Invest и мок со стримами.

Отклонения — в разделе «Отклонения при внедрении» выше: задачи 3.2–3.3 сделаны после группы 8; коды отказов в
`TInvestRefusedException`; «уже в конечном состоянии» при отмене — по чтению состояния; `30057` без известного состояния —
временный отказ; `restored` конца дня — от серии закрытых свечей; кэш расписания до конца суток UTC; найденная сценарием 10.3
ошибка сверки (соседи бота `moex` искались среди ботов спота) исправлена; тексты сессии бота и журнала без имени Bybit.
`ResilienceRegistrationTests` уже проверяли keyed-исполнители по бирже и не менялись; регистрация маршрутизаторов проверяется
переписанным `StreamsRegistrationTests`.

Проверки (последний прогон на HEAD): `dotnet build` — 0 ошибок, 21 предупреждение о nullability в тестах, которые были и до change; `dotnet format --verify-no-changes` — без
изменений; `dotnet test` — Unit 2691, Integration 934, все зелёные (до change: 2535 и 920); фронтенд без изменений
кода — `npm ci`, `lint`, `format:check`, `typecheck`, `test` (1422 теста), `build` зелёные на Node 24. Ни один тест и ни один
запуск не обращался к реальному T-Invest или Bybit: T-Invest — `MockTInvestApi` за `ITInvestCallInvokers`, Bybit — фейки тестовых
хостов; `.env.prod*`, `docker-compose.prod.yml` и продакшен не затрагивались.

## Risks / Trade-offs

- [Единицы `GetPositions.securities.balance` — лоты, а не акции] → пункт чек-листа; маппинг в `TInvestLedger` в одном месте.
- [Стрим не объявляет истечение `DAY`] → сигнал `restored` в конце дня (решение 9) ведёт к сверке и остановке с причиной;
  чек-лист проверяет объявление.
- [`GetOrderState` → `NOT_FOUND` на заявки прошедшего дня] → после ночи без Worker'а бот с истёкшей заявкой встаёт с
  причиной сверки; исполнения того дня не восстанавливаются — владелец сверяет вручную. Принято вместо доказательств по
  операциям ProfitGraph.
- [Идемпотентность `order_id` не такая, как ожидаем (не та же заявка и не `30057`)] → чек-лист; до него `TInvest:Trading`
  выключен.
- [Комиссия не появилась за минуту] → конец заявки с нулевой комиссией; PnL позиции занижен на неё; брокерский отчёт —
  истина. Принято: поздние комиссии дольше минуты — не этот change.
- [Словарь интервалов расписания изменится] → неизвестный день = пауза минута, то есть деградация к нынешнему поведению, а
  не отправка в закрытую сессию.
- [Стрим состояний рвётся каждые 10–30 минут ночью (ADR 0050)] → каждый обрыв — `restored` → `RebuildAsync` → 2–4 унарных
  чтения на бота; лимит `Orders` 50/мин на процесс хватает на десятки ботов; метрика `lost` покажет частоту.
- [Токен стримов свечей — самого старого аккаунта, а торгуют другим] → свечи публичны; удаление того аккаунта переоткрывает
  стрим с новым токеном при следующем обрыве; при одном аккаунте владельца вопроса нет.
- [Второй аккаунт T-Invest на одном токене] → `moex_orders.account_id` различает счета; стрим — на аккаунт; лимитер — на
  токен, как в change 1.

## Migration Plan

1. Слить PR; миграция `moex_orders` применяется при старте API. `TInvest:Trading` по умолчанию `false`: поведение для
   пользователя не меняется, кроме живой свечи на графике бота `moex`.
2. Владелец проходит чек-лист (решение 13) с разрешения, затем ставит `TInvest__Trading=true` в окружении API на проде
   (изменение `.env.prod` — с одобрения) и запускает первого бота минимальным лотом под присмотром.
3. Откат — предыдущий образ: таблица `moex_orders` старому коду не мешает; запущенные боты `moex` перед откатом остановить.

## Open Questions

- Единицы `securities.balance` и поведение идемпотентности `order_id` — решаются чек-листом, маппинг меняется в одном
  классе, спеки и задачи не меняются.
- Нужен ли показ ставки комиссии на странице бота `moex` — мелочь UI, после первого реального запуска.
