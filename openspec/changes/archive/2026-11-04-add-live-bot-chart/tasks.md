# Tasks

Каждая задача с новой логикой идёт red → green → refactor: сначала только тест, запуск и падение по ожидаемой причине,
затем минимальный код, затем правка структуры при зелёных тестах. Кода, которого не требует ни один тест, нет: нужен
новый случай — сначала падающий тест. Деньги, цены и объёмы — `decimal` на сервере и десятичные строки в API; время —
UTC `DateTimeOffset`, текущий момент — только через `TimeProvider`.

## 1. Начало свечи и одна страница Bybit

- [x] 1.1 `IntervalExtensions.CandleOpen(at)` — начало свечи, содержащей момент: сетка от эпохи UTC для минут, часов и
  `D`, понедельник 00:00 для `W`, первое число месяца для `M` — unit-тесты в `Tests.Unit/Domain` на эталонных моментах:
  `12:30` → `12:00` для `60`, `12:07` → `12:05` для `5`, `2024-03-10T12:30Z` → `2024-03-10T00:00Z` для `D`, среда →
  понедельник для `W`, `2024-02-29T23:59Z` → `2024-02-01T00:00Z` для `M`, момент ровно на границе — сам
- [x] 1.2 `IMarketHistoryClient.GetCandlesOnceAsync(instrument, interval, from, to)` в `BybitMarketDataClient` — одна
  страница Bybit до 1000 свечей через `ExecuteOnceAsync` — integration-тесты в `Tests.Integration/Infrastructure` на
  `FakeBybitHandler`: свечи периода разобраны точно, при отказе ровно один запрос и `ExchangeUnavailableException`

## 2. История графика бота

- [x] 2.1 `MarketData/CandleReadThrough` в Application — unit-тесты с NSubstitute-портами и `FakeTimeProvider`: всё
  сохранено — Bybit не вызывается; есть недостающие части — один запрос от начала первой до конца последней; пишутся
  только закрытые свечи из недостающих частей, уже сохранённые и незакрытая — нет; `ExchangeException` — сохранённые
  свечи и признак недоступности, ничего не записано
- [x] 2.2 `Bots/Queries/GetBotCandles` (`GetBotCandlesQuery`, обработчик, `GetBotCandlesResult`) — unit-тесты: неизвестный
  бот — `null`; интервал по умолчанию — интервал бота; ошибки `from`, `to`, `interval` по имени параметра; `from`
  опускается к началу свечи; `to` обрезается до начала текущей свечи; больше 1000 свечей после обрезки — ошибка по
  `from` без вызова Bybit; пустой после обрезки период — пустой результат без вызова Bybit
- [x] 2.3 `GET /api/bots/{id}/candles` в `BotsController` с `BotCandlesResponse` (`interval`, `candles`, `unavailable`) и
  `[ProducesResponseType]` 200/400/404 — integration-тесты в `Tests.Integration/Api/BotCandlesApiTests` на реальном
  Postgres: пустое хранилище — свечи в ответе и затем в `candles`; повтор того же периода не делает kline-запроса;
  Bybit отказывает — 200, сохранённые свечи и `unavailable: true`; задач загрузки не прибавилось; 400 на период длиннее
  1000 свечей и на интервал `2`; 404 на неизвестного бота
- [x] 2.4 Догрузка во время задачи загрузки того же периода — integration-тест: задача загрузки и запросы графика
  идут одновременно, в `candles` ровно одна свеча на каждый open time
- [x] 2.5 Запустить API и `npm run generate:api`, закоммитить `frontend/src/shared/api/generated/schema.d.ts` вместе с
  контрактом — в схеме есть `BotCandlesResponse`, `npm run typecheck` зелёный
- [x] 2.6 `docs/conventions.md` — `GetBotCandles`, `CandleReadThrough`, `GetCandlesOnceAsync`, `Interval.CandleOpen`;
  `CONTEXT.md` — в «Worker» исключение: догрузку истории графика и подписку на текущую свечу выполняет API — документы
  обновлены

## 3. Поток текущей свечи в `BybitStreams`

- [x] 3.1 Порт `Ports/MarketData/IFormingCandleStreams` и запись `FormingCandle` (open time, OHLCV, `IsClosed`,
  `ObservedAt`) — сборка проходит
- [x] 3.2 `BybitStreams` реализует `IFormingCandleStreams`: у kline-топика слушатели всех обновлений рядом со слушателями
  закрытых свечей — integration-тесты в `BybitStreamsTests` на `FakeBybitSocketServer`: слушатель обновлений получает
  `confirm=false` и `confirm=true` с флагом; слушатель закрытых по-прежнему только `confirm=true`; оба на одной серии —
  один `Subscribes`; последний ушедший любого вида снимает топик только после ухода всех; `DropAsync` — `lost`, затем
  `restored`; существующие тесты потоков зелёные без правок
- [x] 3.3 Регистрация `IFormingCandleStreams` на тот же синглтон `BybitStreams` в `InfrastructureRegistrar` — проверка
  разрешения сервисов API и `WorkerHost` зелёная

## 4. Ленты текущих свечей

- [x] 4.1 `MarketData/FormingCandles/FormingCandleThrottle` — unit-тесты на `FakeTimeProvider` по сценариям «Частоты
  обновлений текущей свечи»: первое обновление сразу; пять за секунду — одно, следующее через секунду несёт последнее;
  закрытие сразу; первая свеча нового интервала сразу; обновление со старшим `(OpenTime, ObservedAt)` отбрасывается
- [x] 4.2 `FormingCandleFeeds` — подписчики: первый зритель серии подписывает порт, второй присоединяется, последний
  ушедший снимает подписку; новая подписка того же соединения покидает прежнюю ленту — unit-тесты с фейковым портом
  `IFormingCandleStreams` и `IFormingCandleBroadcast`
- [x] 4.3 `FormingCandleFeeds` — состояние: `Connecting` до первого обновления, `Live` с ним, `NoData` при `lost` и после
  90 с без обновлений, после `restored` — `NoData` до следующего обновления; снимок — состояние и последняя свеча —
  unit-тесты на `FakeTimeProvider`
- [x] 4.4 `FormingCandleFeeds` — повторная подписка после отказа порта или закрытия подписки через 1 с, с удвоением до
  1 мин, пока есть зрители; без зрителей не повторяет — unit-тесты на `FakeTimeProvider`
- [x] 4.5 `docs/conventions.md` — `IFormingCandleStreams`, `FormingCandleFeeds`, `IFormingCandleBroadcast`, только в API —
  документ обновлён

## 5. Хаб графика бота

- [x] 5.1 `Microsoft.AspNetCore.SignalR.Client` в центральные версии пакетов и в `Tests.Integration`; `ProfitFoxFactory`
  получает `BybitSocketAddress` для API — `dotnet build` зелёный, существующие integration-тесты зелёные
- [x] 5.2 `Program`: `AddSignalR`, `MapHub<BotChartHub>("/api/hubs/bot-chart")`; `FormingCandleFeeds` и реализация
  `IFormingCandleBroadcast` над `IHubContext` регистрируются только в API; `BotChartHub` с `Subscribe(botId)` → снимок,
  `Unsubscribe()`, уход при разрыве; сообщения `FormingCandle` и `FormingStatus` с десятичными строками — integration-
  тесты в `Tests.Integration/Api/BotChartHubTests`: снимок и обновления на `FakeBybitSocketServer`; неизвестный бот —
  ошибка `bot_not_found`; две подписки ботов одной серии — один `Subscribes`, обе закрыты — не `Subscribed`; обрыв —
  `NoData`, восстановление и обновление — `Live`; Worker без изменений (проверка разрешения `WorkerHost` зелёная)
- [x] 5.3 Текущая свеча не доходит до стратегии и в хранилище — integration-тест в `LiveTradingTests`: у запущенного
  бота открыт хаб, поток шлёт `confirm=false` его серии — стратегия не получает закрытия бара, в `candles` нет свечи,
  интервал которой не закончился
- [x] 5.4 `docs/conventions.md` — `Hubs` в Presentation, путь хаба под `/api`, сообщения; `CONTEXT.md` — термины
  «Текущая свеча» (незакрытая свеча интервала бота на графике, только в памяти, до стратегии не доходит; _избегать_
  «формирующаяся», «живая свеча») и «График бота» — документы обновлены

## 6. Frontend: чистая логика графика

- [x] 6.1 `@microsoft/signalr` точной версией в `frontend/package.json` и `package-lock.json`; прокси Vite для `/api` с
  `ws: true` — `npm ci` и `npm run build` зелёные
- [x] 6.2 `market-chart/model/chartPaging` — страница 500 свечей перед open time по длине интервала, склейка страниц по
  open time без повторов, «слишком далеко отстали» — Vitest-тесты первыми
- [x] 6.3 `market-chart/model/tradeMarkers` — исполнение на свече, содержащей его, остриё на цене (`atPriceTop` вверх у
  покупки, `atPriceBottom` вниз у продажи), одна подпись «×N» на сторону и свечу, исполнения вне нарисованных свечей и
  после последней не показываются, текущая свеча считается нарисованной — Vitest-тесты первыми
- [x] 6.4 `market-chart/model` — `liveCandles` (текущая свеча поверх истории, свеча с open time не позже последней свечи
  истории не рисуется, прежняя держится до прихода из истории), `volumeBars` (объём из десятичной строки, цвет по
  направлению), `orderLines` (активные лимитные ордера: сторона, цена, остаток) — Vitest-тесты первыми
- [x] 6.5 `bots/model/formingCandle` — типы сообщений хаба, редьюсер (снимок, свеча, статус, потеря связи, сброс;
  старшая свеча игнорируется) и подпись состояния «подключение» / «в реальном времени» / «нет данных» — Vitest-тесты
  первыми
- [x] 6.6 `bots/model/chartIntervals` — `1`, `5`, `15`, `60`, `240`, `D` и интервал бота с «· бот» без дубля; чтение
  `?interval=`: неизвестный и интервал бота — интервал бота, в адрес не пишется — Vitest-тесты первыми

## 7. Frontend: график на странице бота

- [x] 7.1 `market-chart/ui/BotCandleChart` — свечи, объём на скрытой шкале в нижних ~20%, маркеры, пунктирные линии
  ордеров, текущая свеча и её объём через `update`, цвета из токенов темы; запрос назад при приближении видимого
  диапазона к началу — тесты в `charts.test.tsx` с замоканным `lightweight-charts`
- [x] 7.2 `bots`: `botCandlesQuery` — страницы назад, остановка на пустой странице, минутное обновление хвоста и начало
  заново при отставании — тесты через `fakeApi` с фейковыми таймерами
- [x] 7.3 `bots/realtime/followFormingCandle` и `useFormingCandle` — подписка на хаб бота, повторная подписка после
  переподключения, отписка при уходе со страницы, потеря связи — «нет данных» — тесты с замоканным `HubConnection`
- [x] 7.4 `pages/bot/BotChart` над `LiveControls`, `?interval=` в `validateSearch` маршрута бота; строка последней свечи
  с «не закрыта»; состояние канала на интервале бота и пометка о минутном обновлении на другом; «История недоступна:
  Bybit не отвечает»; доступное описание с числом исполнений и ордеров — тесты в `pages/bot/bot.test.tsx` через
  `renderApp`: график у остановленного бота, выбор «1 д» пишет `?interval=D`, другой интервал — без текущей свечи и с
  пометкой, `unavailable` — сообщение при видимом статусе и позиции
- [x] 7.5 `docs/conventions.md` — модули `market-chart` (график бота) и `bots` (история, хаб, переключатель) — документ
  обновлён

## 8. End-to-end

- [x] 8.1 Заглушка Bybit: `/control/forming?symbol&interval&start&close&volume` шлёт в kline-топик свечу с
  `confirm=false` — существующие e2e зелёные
- [x] 8.2 `e2e/bot-chart.spec.ts` на запущенном боте с аккаунтом заглушки: история с объёмом в строке последней свечи →
  `/control/forming` — «не закрыта», «в реальном времени» и новые цена и объём → `/control/candle` той же свечи — «не
  закрыта» пропадает → `/control/fill` — в описании графика исполнение → «5 мин» — `?interval=5`, пометка о минутном
  обновлении, без «не закрыта» — `npm run test:e2e` зелёный

## 9. Итоговая проверка

- [x] 9.1 Пройти diff: каждая ветка, условие и проверка нового кода требуются тестом; лишнее удалить или сначала
  написать тест — список проверенных мест в описании PR
- [x] 9.2 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` (и с `TZ=Asia/Tokyo`),
  `dotnet format backend/ProfitFox.sln --verify-no-changes`; во `frontend/` — `npm run lint`, `npm run format:check`,
  `npm run typecheck`, `npm test`, `npm run build`, `npm run test:e2e` — все зелёные

## Workflow follow-up

- Ревью владельцем решений агента в `design.md` (одна попытка к Bybit, исполнения и ордера из опроса позиций, остаток
  в подписи ордера, переподключение потока, строка последней свечи и доступное описание графика).
- `/opsx:archive` перед pull request, чтобы код и обновлённые `openspec/specs` попали в `main` одним squash-коммитом.
