# Tasks

Каждая задача — красный тест, минимальный код, рефакторинг. Unit-тесты в `Tests.Unit` зеркалят папки `src`; адаптер
проверяется на `Tests.Shared/TInvest/MockTInvestApi`; ни один тест и ни один локальный запуск не обращаются к реальному
T-Invest. Решения — в design.md, требования — в `specs/`.

## 1. Домен и порты

- [x] 1.1 `ExchangeKey` получает `Exchange` (по умолчанию `Bybit`); `ToString` по-прежнему без секрета. Тест: ключ T-Invest
  печатает номер счёта и биржу, не токен.
- [x] 1.2 `OrderOutcome.RetryAfter` (`TimeSpan?`); `LiveOrder.Deferred(now, TimeSpan? notBefore = null)` назначает
  `NextAttemptAt` не раньше `now + notBefore` и не раньше нарастающей паузы; `IdeaTradeOrder.Deferred` получает тот же
  необязательный параметр. Тесты: пауза биржи длиннее нарастающей побеждает; без неё поведение прежнее (существующие тесты
  зелёные).
- [x] 1.3 `LiveRequests.KeyAsync` строит ключ по бирже аккаунта: Bybit — `ApiKey` + секрет, T-Invest — `AccountId` + токен,
  `Exchange` аккаунта. Тест на обе биржи.

## 2. Запуск бота на MOEX

- [x] 2.1 `Application/LiveTrading/MoexTradingSettings(Enabled)`; `TInvestSettings.Trading` (`bool`, `false`) читается в
  `ConfigureTInvest` и регистрируется для API. Тест регистрации: без секции `Enabled == false`.
- [x] 2.2 `StartBotCommandHandler`: бот `moex` при выключенной торговле — `ConflictException("Trading on MOEX is disabled.")`
  до обращений к портам; конфликт «not supported yet» удалён. Тесты: отказ без вызовов `IBrokerAccountReader` и
  `IExchangeLedger`; при включённой — запуск идёт дальше.
- [x] 2.3 Проверка счёта T-Invest при запуске: `IBrokerAccountReader.ListAccountsAsync(token)` → счёт аккаунта найден, открыт,
  полный доступ (`BrokerAccountRules.Problem`), иначе конфликт с причиной; `IExchangeKeyInspector` и hedge — только для
  Bybit. Тесты: счёт только для чтения → конфликт; счёт пропал из списка → конфликт; Bybit без изменений.
- [x] 2.4 Деньги при запуске на `moex`: `GetFreeAsync(key, "RUB")` через порт; тест `StartBot` с `50 000` RUB, из них `10 000`
  заблокированы, и балансом `45 000` → «Not enough free money».
- [x] 2.5 Интеграционный тест API: `POST /api/bots/{id}/start` бота `moex` при выключенной торговле → 409 с причиной; при
  включённой (`TInvest:Trading=true` в тестовом хосте) и моке счёта → 204 и бот «запущен». Спека `live-trading`
  «Торговля на MOEX выключателем» и «Запуск бота» покрыты.

## 3. Маршрутизация портов торговли

- [x] 3.1 `Exchanges/Routing/RoutedOrderGateway`, `RoutedExchangeLedger`, `RoutedExchangeStreams` (по `key.Exchange`;
  `SubscribeCandlesAsync` — по `Category.Exchange()`), `RoutedFormingCandleStreams` (по категории); реализации Bybit и T-Invest
  keyed по `Exchange`. Unit-тесты: вызов с ключом T-Invest уходит в реализацию T-Invest, с ключом Bybit — в Bybit.
- [ ] 3.2 Регистрация в `InfrastructureRegistrar`: маршрутизаторы — единственные реализации четырёх портов; `BybitStreams`
  keyed `Bybit` для обоих интерфейсов стримов; `StreamsRegistrationTests` и `ResilienceRegistrationTests` переписаны под
  keyed; `LiveTradingScenario` с `FakeExchange` зелёный без изменений. Выполняется после группы 8 (design.md, «Отклонения при
  внедрении»).
- [ ] 3.3 `FormingCandleFeeds.JoinAsync` больше не отвечает `NoData` для T-Invest: подписка идёт в порт. Тест: серия T-Invest
  подписывает `IFormingCandleStreams`; прежний тест на `NoData` удалён. `docs/conventions.md`: абзац о `FormingCandleFeeds`
  и о маршрутизации портов торговли. Выполняется после группы 8 (design.md, «Отклонения при внедрении»).

## 4. Идентификаторы и состояния заявок T-Invest

- [x] 4.1 Миграция `AddMoexOrdersMigration`: таблица `moex_orders` по design.md (решение 3); интеграционный тест
  `TInvestOrderIdsTests` в `Tests.Integration/Persistence`: запись, чтение по `client_id` и по `request_id`, дописывание
  `exchange_order_id`, уникальность `request_id`.
- [x] 4.2 `Exchanges/TInvest/TInvestOrderIds` (над `ITransactionExecutor`/Dapper, как репозитории): `EnsureAsync(clientId,
  accountId, uid)` возвращает существующий или новый случайный `request_id`; `ClientIdOf(requestId)`; `SetExchangeIdAsync`.
  Тест: повторный `EnsureAsync` отдаёт тот же `request_id`.
- [x] 4.3 `TInvestShare.Exchange` (поле `exchange` акции) в `TInvestShares.Tradable`; тест маппинга.
- [x] 4.4 `TInvestOrderStates`: `ExchangeOrder` из `OrderState` (объём лоты × лот, стоимость Σ stages, комиссия
  `executed_commission`, состояния `NEW`/`PARTIALLYFILL` → `Open`, `FILL` → `Filled`, `CANCELLED` → `Cancelled`, `REJECTED` →
  `Rejected`), `ExchangeFill` из `stages`; расхождение Σ stages с `lots_executed` — признак «перечитать». Unit-тесты на каждом
  состоянии и на расхождении.

## 5. Статус торгов, коридор, расписание

- [x] 5.1 `TInvestTradingStatuses`: `GetTradingStatus` и `GetOrderBook(depth 1)` с кэшем 5 с на `uid`; `MayPlace(uid, type,
  price)` → пропуск / «сессия закрыта» / «цена вне коридора». Unit-тесты на моке: флаги типа заявки, пустой и
  перевёрнутый коридор, кэш (второй вызов в 5 с не ходит к бирже).
- [x] 5.2 Расписание: `TradingSchedules(exchange, сегодня..+7)` с кэшем до конца дня; `NextSessionStart(exchange, now)` по
  словарю интервалов (design.md, решение 6); нечитаемое расписание или неизвестный тип → `null`. Unit-тесты: ночь → утро
  `06:50` МСК; `dealer_*` не считается; неизвестный тип → `null`; `30003` на прошлую дату не роняет.

## 6. Гейтвей заявок T-Invest

- [x] 6.1 `TInvestOrderGateway.PlaceAsync`: лоты (не целое → `Permanent`), `DAY` для лимитной, рыночная без цены, `Protection`
  / `ReduceOnly` → `Permanent`, `request_id` из `TInvestOrderIds` до отправки, `exchange_order_id` в таблицу после. Unit-тесты
  на моке: записанный запрос `PostOrder` (счёт, uid, лоты, `Quotation`, `time_in_force`), повтор с тем же `order_id`.
- [x] 6.2 Таблица отказов (решение 5): `30057` → `Accepted` с чтением состояния; `30098`/статус → `Transient` с `RetryAfter` до
  сессии (минута без расписания); `30099`/коридор → `Transient` минута; постоянные коды → `Permanent` с кодом без текста
  брокера; транспортные статусы → `Transient`; `UNAUTHENTICATED` → `Permanent`. Unit-тест на каждую строку таблицы.
- [x] 6.3 `CancelAsync` по `OrderIdType.Request`, `CancelByExchangeIdAsync` по `Exchange`; `NOT_FOUND` и «уже в конечном
  состоянии» → `AlreadyDone`; иной отказ → `Permanent`. Unit-тесты.
- [x] 6.4 Проверка перед отправкой: `MayPlace` вызывается до `PostOrder`; закрытая сессия — ни одного `PostOrder` в записи
  мока. Unit-тест.

## 7. Леджер T-Invest

- [x] 7.1 `TInvestLedger.GetFreeAsync` (рубли минус заблокированные из `GetPositions`), `IsHedgeModeAsync` → `false`,
  `GetHoldingAsync` (`balance + blocked` по `uid`), `GetOpenOrdersAsync` (`GetOrders` по `uid`, `ClientId` через таблицу, чужая
  → `""`). Unit-тесты на моке.
- [x] 7.2 `FindOrderAsync` (строка → `GetOrderState` по `request_id`, `NOT_FOUND` → `null`, без строки → `null` без вызова),
  `FindOrderByExchangeIdAsync`, `GetFillsAsync` / `GetOrderFillsAsync` из `stages`, `GetOrdersOfParentAsync` → пусто.
  Unit-тесты.
- [x] 7.3 `GetFeesAsync` через `GetLastPrices` + `GetOrderPrice` (лоты на ≥ 100 000 ₽): `FeeSchedule(rate, rate, Moex)`; ни одного
  `PostOrder` в записи мока. Unit-тест с `deal_commission 50` на `100 000` → `0.0005`.

## 8. Стримы T-Invest

- [x] 8.1 `MockTInvestApi`: `OnServerStream` и `OnDuplex` со скриптом (канал ответов, запись запросов и входящих сообщений,
  `Complete()`, `Fail(status)`); `MockTInvestApiTests` на оба вида.
- [x] 8.2 `TInvestStreams.SubscribeAccountAsync`: один `OrderStateStream` на аккаунт с `ping_delay_millis`; сообщение → чтение
  `GetOrderState` по `request_id` до 5 раз с паузой секунда при `NOT_FOUND` → `ExchangeOrder` и новые `ExchangeFill`
  слушателям; без строки — чужая заявка (`ClientId = ""`, инструмент по тикеру). Unit-тесты: два слушателя — один стрим;
  событие раньше состояния; чужая заявка; последний слушатель закрывает стрим.
- [x] 8.3 Поздняя комиссия: при `FILL` с нулевой комиссией состояние перечитывается через 5, 15, 30, 60 с (`TimeProvider`),
  конец заявки уходит с первой ненулевой комиссией или через минуту с нулём. Unit-тесты на `FakeTimeProvider`.
- [x] 8.4 Переподключение стрима заявок: конец или ошибка → паузы 1, 2, 5, 10, 30 с; тишина 90 с без пингов → переоткрытие;
  после восстановления `restored` каждому слушателю; конец торгового дня по расписанию (+1 мин) → `restored`. Unit-тесты.
  Сигнал конца дня приходит слушателям закрытых свечей серии (design.md, «Отклонения при внедрении»).
- [x] 8.5 Стримы свечей: два `MarketDataStream` на процесс (`waiting_close` true/false, токен `TInvestTokenSource`,
  `PingSettings 30 с`), подписка на серию однажды, отписка последним слушателем, `Candle` с объёмом × лот и оборотом как у
  истории; переподключение с повторной подпиской и `restored`. Unit-тесты: две подписки в одном стриме; закрытая свеча
  только из «закрытого» стрима; обрыв → повторная подписка.
- [x] 8.6 Метрики: открытие стрима — запрос его группы (`outcome` `ok` / `error`), обрыв — `lost`. Unit-тест на `MeterListener`.
- [x] 8.7 `docs/conventions.md`: раздел `Exchanges/TInvest` дополнен гейтвеем, леджером, стримами, `TInvestOrderIds`,
  `TInvestTradingStatuses`, `TInvestOrderStates`; мок — стримами.

## 9. Сессия и пауза биржи

- [ ] 9.1 `LiveBotSession.SendAsync` передаёт `outcome.RetryAfter` в `Deferred`; будильник `WakeInput` на `NextAttemptAt`.
  Тест на `FakeExchange`: `Transient` с `RetryAfter` 5 ч → следующая попытка не раньше, бот «запущен».
- [ ] 9.2 Отмена отложенной заявки без `exchange_order_id` закрывается без вызова биржи (существующее поведение) — тест
  для T-Invest-пути через `FakeExchange`: `FindOrderAsync` → `null` → заявка отменена.

## 10. Интеграция

- [ ] 10.1 Сценарий Worker'а на `MockTInvestApi` «бот через ночь» (`Tests.Integration/Workers`): бот `moex` / `SBER` / `60`
  на аккаунте T-Invest; закрытая свеча из стрима → `PostOrder` дневной лимитки → `NEW` в стриме → `CANCELLED` в конце дня →
  стратегия перевыставляет → статус «закрыто» → `NextAttemptAt` = открытие по расписанию → часы вперёд → `PostOrder`. Ни одного
  `PostOrder` между закрытием и открытием.
- [ ] 10.2 Сценарий «исполнение с поздней комиссией»: `FILL` с комиссией `0`, через 10 с `1.25` → позиция с комиссией `1.25`.
- [ ] 10.3 Сценарий сверки: активная заявка бота, которой биржа не знает (`NOT_FOUND`), после `restored` → бот «ошибка» с
  причиной, называющей заявку.
- [ ] 10.4 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; фронтенд без изменений кода — `npm run lint`,
  `format:check`, `typecheck`, `test`, `build` зелёные.

## 11. Владелец

- [ ] 11.1 Чек-лист design.md (решение 13) на реальном счёте с разрешения владельца, по шагам; результаты — поправкой в
  design.md; расхождения с решениями 5–9 исправлены до включения `TInvest:Trading`.
- [ ] 11.2 Ручная проверка в dev: бот `moex` / `SBER` запускается при `TInvest:Trading=true` на моке или не запускается с
  причиной при `false`; на странице бота живая свеча в сессию.

## Workflow follow-up

- Ревью владельца, `/opsx:archive`, PR с squash.
- Включение `TInvest__Trading=true` на проде — отдельным одобрением владельца после чек-листа; первый бот минимальным лотом.
