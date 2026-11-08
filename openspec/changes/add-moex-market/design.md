# Design

## Context

Мотивация — в proposal.md. Решения о месте T-Invest в системе приняты на разборе 2026-11-08 (grill-me) и зафиксированы
здесь как решения 1–6; технические выборы по коду — решения 7–18. Как устроено сейчас (`main` 018b73e):

- `Instrument(MarketCategory, Symbol)`, `MarketCategory { Spot, Linear }`; коды `spot` / `linear` в `MarketDataCodes` служат и
  API, и хранилищу (19 таблиц с колонкой `category`, маршруты `/{category}/{symbol}/…`). Список интервалов один на всё —
  интервалы Bybit.
- `ExchangeAccount(Id, Name, ApiKey, Uid, Categories, CreatedAt)`; таблица `exchange_accounts` с `environment`, `uid`,
  `categories <@ {spot, linear}`, секрет в трёх колонках AES-GCM (`AesGcmSecretProtector`, id аккаунта как associated data).
  `CreateExchangeAccountCommandHandler` проверяет ключ через `IExchangeKeyInspector.InspectAsync` и `ExchangeKeyRules`.
- Порты чтения без ключа: `IMarketHistoryClient`, `IInstrumentRulesProvider`, `IInstrumentCatalog` (под
  `CachedInstrumentCatalog`); все реализованы одним `BybitMarketDataClient`. `IExchangeKeyInspector.GetBalanceAsync(apiKey, secret)`
  отдаёт `AccountBalance` в USD.
- `CandleReadThrough` и `RunNextCandleImportCommandHandler` считают пропущенным каждый слот сетки интервала
  (`Interval.MissingRanges` по сохранённым open time) и запрашивают его у биржи заново при каждом чтении.
- `BacktestReplay` идёт по сохранённым свечам одна за другой; количество округляется вниз к `InstrumentRules.QuantityStep`
  и отклоняется ниже `MinQuantity`, так что лот = шаг уже работает. `FeeSchedule.AssetOf` берёт базовую монету только для
  покупки на `Spot`, иначе котировку. `BacktestSettings.DefaultFees` — словарь по кодам категорий из настроек.
- Запуск бота (`StartBotCommandHandler`) проверяет ключ Bybit через `IExchangeKeyInspector` и `IExchangeLedger`.
- Метрики Bybit REST: `BybitRestMetricsHandler` на `Meter("ProfitFox.Bybit")`, гейджи остатков в `BybitLimitHeadroom`.
- T-Invest Invest API: gRPC, без анонимного доступа; лимиты на токен в минуту по группам сервисов; `GetCandles` с
  источником `Exchange` отказывает на параметр `limit` (код 30220) и ограничивает окно запроса интервалом; свечи в лотах;
  сертификат сервера выпущен Russian Trusted Sub CA (истекает 2027-03-06), которого нет в доверенных корнях образов
  `mcr.microsoft.com/dotnet/aspnet:10.0`. Опыт трёх прошлых реализаций владельца (profit-fox-old, Designer, ProfitGraph
  `Venues.TInvest`, ADR 0023 и продолжения) — источник чисел и граблей ниже.

## Goals / Non-Goals

**Goals:**

- Третий рынок входит в модель без изменения ключей таблиц, маршрутов и формы портов чтения.
- Все чтения T-Invest идут одним путём: токен → лимитер группы методов → канал → метрики, и ни один запрос не уходит без
  аккаунта T-Invest в хранилище.
- Ночи и выходные MOEX не стоят ни одного запроса после первой загрузки.
- Bybit ведёт себя байт-в-байт как раньше: результаты бэктестов, догрузка, формы.

**Non-Goals:**

- Заявки, стримы заявок, сверка, живые свечи для ботов на MOEX (change `add-moex-live-bots`).
- Сканер, страница «Рынок» и идеи для `moex` (change `add-moex-ideas`).
- Общий лимитер между процессами (Redis): остаётся отложенным, как и для Bybit.
- Дневные свечи `D` на `moex` и свечи выходных сессий как особый случай.

## Decisions

### 1. `Moex` — третья категория рынка, биржа выводится из категории

`MarketCategory.Moex` с кодом `moex`; символ — тикер в верхнем регистре (`SBER`), как `Instrument` и так нормализует. В
Domain появляется `Entities/ExchangeAccounts/Enums/Exchange { Bybit, TInvest }` и чистое расширение
`MarketCategoryExtensions.Exchange()`: `Spot`, `Linear` → `Bybit`, `Moex` → `TInvest`. Биржа нигде не хранится у
инструмента и не ходит по API.

Почему не отдельная ось «биржа» у `Instrument`: код ключуется по рынку, а не по паре «биржа × тип продукта» — spot и
linear на Bybit уже разные рынки с разными стримами, правилами и экономикой. Третий рынок в тот же плоский список
ничего не теряет и не трогает 19 таблиц и маршруты. Слово «категория» начинает значить «рынок»; переименование
`MarketCategory` → `Market` возможно позже отдельным рефакторингом. Отдельная ось оправдана, только если один тип продукта
появится на нескольких биржах сразу, чего в планах нет.

Интервалы — свойство категории: `MarketDataCodes.IntervalsOf(category)` (Bybit — все 13; `Moex` — `1`, `3`, `5`, `15`, `30`,
`60`, `120`, `240`). Тексты ошибок «Interval must be one of …» строятся из этого списка. `D` для `Moex` исключён, пока
чек-лист не подтвердит, что дневные свечи T-Invest открываются на нашей UTC-сетке (MOEX открывается в 03:50 UTC).

### 2. Один аккаунт T-Invest: токен и для данных, и для торговли

`ExchangeAccount` получает `Exchange` и поля биржи: для `Bybit` — `ApiKey`, `Uid`; для `TInvest` — `AccountId` (номер
брокерского счёта). Домен держит одну плоскую запись с проверкой инвариантов в конструкторе (у аккаунта Bybit есть ключ
и UID, у T-Invest — номер счёта), а не иерархию: потребителей аккаунта пять, и все они смотрят на `Categories`. Токен
хранится в тех же трёх колонках секрета, тем же `ISecretProtector`.

Таблица: `exchange` text NOT NULL DEFAULT `'bybit'` (default снимается после заполнения), `api_key` и `uid` становятся
nullable, `account_id` text NULL, CHECK категорий расширяется на `moex`, уникальность `(uid, environment)` заменяется двумя
частичными индексами: `(uid) WHERE exchange = 'bybit'` и `(account_id) WHERE exchange = 'tinvest'`.

Создание по бирже: `CreateExchangeAccountCommand` получает `Exchange`, `Token`, `AccountId`; для T-Invest обработчик
читает счета токена через новый порт `Ports/ExchangeAccounts/IBrokerAccountReader.ListAccountsAsync(token)` и принимает
только `Open` + `FullAccess` (типы брокерский и ИИС). Повторное чтение при сохранении защищает от доступа, отозванного
между списком и сохранением (ProfitGraph TC5). Баланс: `IBrokerAccountReader.GetPortfolioAsync(token, accountId)`;
`AccountBalance` получает валюту (`USD` для Bybit, `RUB` для T-Invest), и UI подписывает суммы ей.

Почему не `IVenueCredentialSchema` ProfitGraph, где биржа сама объявляет поля формы: бирж две, форма с переключателем
проще и честнее, чем мета-описание полей.

### 3. Источник токена для чтений без счёта

`Infrastructure/Exchanges/TInvest/TInvestTokenSource`: берёт самый старый аккаунт `TInvest` из `IExchangeAccountRepository`,
расшифровывает секрет и держит его в памяти до 5 минут; без аккаунта бросает `ExchangeUnavailableException` с текстом
«данные T-Invest недоступны», и запрос не уходит. Порты чтения не меняют сигнатур: `TInvestMarketDataClient` сам
спрашивает источник. Удалённый аккаунт перестаёт использоваться не позже чем через 5 минут (спецификация
`exchange-accounts`).

Отвергнуто: токен в переменной окружения (секрет мимо зашифрованного хранилища и UI) и учётные данные вызывающего во
всех портах чтения (ProfitGraph TC6: меняет форму всех портов, не отвечает, чем читает Scanner и график без счёта, и дал
баг с `AsyncLocal`).

Один токен делит лимит `MarketData` (300/мин по документации) между API, Worker и Scanner, и лимитер у каждого процесса
свой. Бюджеты в `appsettings` каждого хоста: API 100, Worker 150, Scanner 50 (Scanner T-Invest не читает до change 3).
Сумма ниже 300; при `RESOURCE_EXHAUSTED` группа встаёт на паузу по трейлеру `x-ratelimit-reset` (1–3600 с, иначе минута).

### 4. Граница покрытия вместо дозагрузки по сетке

`ValueObjects/MarketData/SeriesCoverage(From, Until)`: внутри `[From, Until)` хранилище содержит всё, что биржа отдала;
чистая функция `Extend(from, to)` расширяет границы, когда новый диапазон касается или пересекает текущий, иначе
оставляет их (следующее чтение запросит кусок ещё раз). Хранится в таблице `candle_coverage (category, symbol, interval,
covered_from, covered_until)` через `ISeriesCoverageRepository` (Get, Upsert).

Пропущенные диапазоны считаются как раньше (`Interval.MissingRanges` по сохранённым open time), а затем из них вычитается
покрытие. Для Bybit ничего не меняется: у него нет дыр, пропущенных диапазонов нет, строки покрытия не нужны. Для Moex
первое чтение или импорт запрашивает период целиком, двигает границу до `min(to, конец последней закрытой свечи)`, и
ночи больше не считаются пропуском. Граница двигается в той же единице работы, что и запись свечей.

**Поправка после ревью (граница растёт на весь проверенный период).** `Extend` для отрезка, не касающегося покрытия, оставляет
его прежним, поэтому расширять покрытие только на отрезок пропусков нельзя: сохранённые дни между пропусками «разрывали»
касание, и ночи, отделённые от покрытия сохранёнными днями, не покрывались никогда (открыли график бота — покрытие у «сейчас»,
импорт за год — ни одна ночь его не касается, год остаётся непокрытым). Теперь read-through расширяет покрытие на весь
запрошенный период `[from, min(to, граница))`: внутри него всё либо сохранено, либо покрыто, либо только что запрошено. Импорт
расширяет его нарастающим итогом `[job.From, min(конец диапазона, граница))` после каждого загруженного диапазона, так что
прогресс сохраняется при сбое. Отдельный период, не касающийся покрытия, по-прежнему остаётся вне его — одна граница на
серию, как и было решено; следующее чтение запросит такой кусок ещё раз.

**Поправка после ревью (запас границы).** Адаптер отбрасывает свечи с `is_complete == false`; если биржа отдаёт только что
закрытую свечу ещё незавершённой, граница, дошедшая до её конца, спрятала бы её навсегда. Граница — и в read-through, и в
импорте — не заходит дальше `CandleOpen(now − SeriesCoverage.Margin)`, где `SeriesCoverage.Margin` = 1 минута, одна константа
в домене. Для Bybit ничего не меняется: покрытия у него нет, запас 5 с read-through на сохранение свечей остаётся прежним.

Ничего не синтезируется: ни таблицы закрытий, ни плоских свечей (ProfitGraph TC1/TC2 и эталонная акция SBER не нужны).
Стратегия и индикаторы видят только реальные свечи, живые и в реплее одинаково; тихая минута внутри сессии не считается
баром. «Возраст позиции» в барах считается по реальным свечам. Покрытие сканера (`ScannerPair.CoveredUntil`) остаётся
своим: там минуты, здесь серии любого интервала.

### 5. Заявки — вне этого change, но модель готова

Решения разбора для следующего change записаны здесь, чтобы адаптер этого change их не нарушал: истечение DAY-заявки —
обычный `Cancelled` биржи (движок перевыставляет через `HoldLimit`), закрытая сессия и коридор — `Transient` с причиной,
защита сделки — две стоп-заявки брокера `GoodTillCancel`, идемпотентность — таблица `moex_orders` со случайным UUID
запроса. В этом change адаптер не реализует `IOrderGateway`, `IExchangeLedger`, `IExchangeStreams`; запуск бота на `moex`
отклоняется в `StartBotCommandHandler` до любого обращения к бирже.

**Поправка после ревью (аккаунт T-Invest в идеях).** Настройки торговли по идеям и автомат проверяли только существование
аккаунта, а дальше `IdeaTradeEntryCalculator.AccessAsync` → `LiveRequests.KeyAsync` строил `ExchangeKey(account.ApiKey!, токен)` и шёл
в Bybit с `null`. Теперь `IdeaTradeAccounts.CanTrade` (только `Exchange.Bybit`, до change `add-moex-ideas`) отклоняет аккаунт при
сохранении настроек и автомата (ошибка по полю `accountId`), а `AccessAsync` для такого аккаунта отдаёт `NoKey`, не читая секрет и
не обращаясь к Bybit.

**Поправка после ревью (живая свеча бота на `moex`).** `IFormingCandleStreams` реализован только `BybitStreams`, и страница бота
`moex` / `SBER` подписывалась на `kline.<interval>.SBER` у Bybit linear, получала отказ и повторяла подписку по `ScheduleRetry`, пока
открыта. Теперь `FormingCandleFeeds.JoinAsync` для инструмента с `Category.Exchange() == Exchange.TInvest` сразу отдаёт `NoData`,
ничего не подписывая (и снимает зрителя с его прежней серии); живые свечи T-Invest — change `add-moex-live-bots`.

### 6. Проверка: мок и чек-лист на реальном счёте, без песочницы

Unit- и интеграционные тесты идут на in-process моке gRPC (`Tests.Shared/TInvest/MockTInvestApi` — `CallInvoker` с
обработчиками по имени метода, запись вызовов и метаданных, по образцу ProfitGraph). Чек-лист чтений на реальном
счёте выполняется с разрешения владельца его токеном и только читающими методами; результаты дописываются в раздел
«Чек-лист» ниже. Песочница T-Invest не используется.

### 7. Адаптер в `Infrastructure/Exchanges/TInvest` на `Tinkoff.InvestApi`

Пакет `Tinkoff.InvestApi` (генерированные клиенты `Tinkoff.InvestApi.V1`, без фасада SDK), версия фиксируется в
`Directory.Packages.props` при внедрении (ProfitGraph — 0.6.25). Состав:

- `TInvestSettings` (секция `TInvest`): `Endpoint` (по умолчанию `https://invest-public-api.tbank.ru:443`, принимается
  только официальный хост или хост `.invalid` в тестах), `AppName` (`profit-fox`, заголовок `x-app-name`), `RequestTimeout`
  (10 с), `Limits` по группам (`Users` 50, `Instruments` 150, `MarketData` по хосту, `Operations` 100, `Orders` 50),
  `Certificates` (путь к файлам и два отпечатка SHA-256).
- `TInvestChannel`: один `GrpcChannel` на процесс, `SocketsHttpHandler` с проверкой цепочки: системное доверие или цепочка
  до закреплённого корня через закреплённый Sub CA (файлы `.cer` рядом с приложением, отпечатки из настроек).
  Переключение на новый Sub CA — правка настроек и файла, без релиза кода. Тестовая замена — `ITInvestCallInvokers`,
  который отдаёт `CallInvoker`; мок подменяет его в DI.
- `TInvestCall`: унарный вызов = допуск лимитером → метаданные (`authorization: Bearer`, `x-app-name`) → дедлайн →
  метрики → перевод `RpcException` в исключения Application: `Unauthenticated` / `PermissionDenied` →
  `ExchangeKeyRejectedException`; `ResourceExhausted` → пауза группы и `ExchangeRateLimitedException`; `Unavailable`,
  `Internal`, `Aborted`, `DeadlineExceeded` → `ExchangeUnavailableException`; `NotFound`, `InvalidArgument` и прочее →
  `ExchangeException` с кодом из описания статуса, если оно — число. Текст брокера в сообщения не попадает. Чтения
  повторяются лестницей `ExchangeResilienceExecutor`, как у Bybit; записей в этом change нет.
- `TInvestRequestLimiter`: скользящее окно в минуту на (отпечаток токена, группа) и пауза до `x-ratelimit-reset`; ожидание
  до отправки, так что запрос никогда не ждёт после того, как мог уйти.
- `TInvestValues`: `Quotation`/`MoneyValue` ↔ `decimal` как `units + nano / 1e9`, без `double`; `Timestamp` ↔ UTC.
- `TInvestShares`: разрешение тикера в акцию (`ShareBy(ticker, TQBR)`, кэш 1 час на тикер): uid инструмента, лот, шаг цены,
  валюта, флаги API-торговли и шорта, название. Запросы свечей и цен идут по uid.
- `TInvestMarketDataClient : IMarketHistoryClient, IInstrumentRulesProvider, IInstrumentCatalog`: `GetCandles` окнами по
  интервалу (`1`/`3` мин — сутки, `5` — неделя, `15`/`30` — три недели, часовые — 90 дней), источник `Exchange`, без
  `limit`; только `is_complete`; объём × лот; оборот = объём × `(high + low + close) / 3`. `GetCandlePagesAsync` отдаёт
  окна от конца периода к началу, самые новые первыми, как Bybit. `InstrumentExistsAsync` — через `TInvestShares`.
  `GetLastPriceAsync`/`GetLastPricesAsync` — `GetLastPrices`. Каталог — `Shares(INSTRUMENT_STATUS_BASE)` с фильтром TQBR,
  `rub`, `api_trade_available_flag`, лот и шаг цены положительны; `ListedInstrument` получает `ShortEnabled` и `Name`
  (поиск по названию) при `TradFi = true`, `BaseCoin` = тикер, `QuoteCoin` = `RUB`.
- `TInvestAccountClient : IBrokerAccountReader`: `GetAccounts` (номер, название, тип, статус, доступ), `GetPortfolio` в рублях.
  Поправка после ревью: строка «RUB» бралась из `total_amount_currencies` (все валюты в рублях), а валютные позиции отфильтровывались —
  доллары показались бы количеством рублей. Теперь каждая валютная позиция — своя строка по ISO-коду (первые три буквы тикера,
  `USD000UTSTOM` → `USD`), стоимость — количество × `current_price` в рублях (для RUB — количество), общая стоимость — из
  `total_amount_portfolio`.
- `TInvestMetrics`: `Meter("ProfitFox.TInvest")`, счётчик `profitfox.tinvest.requests` {`group`, `method`, `outcome`} и
  гейджи `profitfox.tinvest.limit` / `profitfox.tinvest.limit_remaining` {`group`} из заголовков `x-ratelimit-limit` /
  `x-ratelimit-remaining`; `MetricsRegistrar` добавляет meter.

**Поправка после ревью (отмена вызова).** Канал создавался без `ThrowOperationCanceledOnCancellation`, и отмена токена во время
вызова приходила как `RpcException` со статусом `Cancelled`, который `Translate` превращал в `ExchangeException` и считал в метриках
как `error`. Теперь канал бросает `OperationCanceledException` сам, а `TInvestCall` для `Cancelled` при отменённом токене
пробрасывает `OperationCanceledException` без учёта в метриках.

**Поправка после ревью (circuit breaker на биржу).** Один экземпляр `ExchangeResilienceExecutor` на обе биржи делил и circuit
breaker (10 запросов, 50 % отказов, пауза 15 с): недоступный T-Invest (шесть отказов одной лестницы ретраев импорта) открывал
цепь для Bybit, и в торгующем Worker падали `GetLastPriceAsync` автоматов и прогрев истории. Теперь executor регистрируется keyed
по `Exchange`, по экземпляру на биржу: `TInvestCall` берёт экземпляр T-Invest, `BybitMarketDataClient` и `ScannerRestGate` — Bybit.

### 8. Маршрутизация портов чтения по категории

`Infrastructure/Exchanges/Routing/RoutedMarketHistoryClient`, `RoutedInstrumentRulesProvider`, `RoutedInstrumentCatalog`:
каждая реализация держит Bybit и T-Invest и выбирает по `instrument.Category.Exchange()`. Зарегистрированные порты — эти
маршрутизаторы; `CachedInstrumentCatalog` остаётся сверху и кэширует список `moex` на тех же правах. Вызовы без
инструмента (`GetLastPricesAsync(category)`, `ListAsync(category)`) маршрутизируются по категории напрямую.

Отвергнуто: `switch` по категории внутри каждого обработчика Application — размножает знание о биржах по слою, который
по конвенции не знает вендоров.

### 9. Бэктест и бот на `moex`

Правила акции через `IInstrumentRulesProvider`: `TickSize` = `min_price_increment`, `QuantityStep` = `MinQuantity` = лот,
`MinAmount` = 0. Реплей уже округляет к шагу и отклоняет ниже минимума; `FeeSchedule.AssetOf` даёт котировку для `Moex`
без правок (условие — только `Spot`). `BacktestSettings.DefaultFees` обязана содержать `Limit` и `Market` для каждой
категории (`ValidateOnStart`), `moex` по умолчанию `0.05` / `0.05` (тариф «Трейдер»; владелец правит в настройках после
`GetOrderPrice` из чек-листа); `Slippage` для `moex` заполняется `0.1` как заглушка до change 3, `HasIdeaSlippage`
по-прежнему требует только `spot` и `linear`. Шорт на `moex` отклоняется, как на `spot` (`BotRequests`): маржа —
решение change 4. Проверка аккаунта бота уже сравнивает `Categories`, аккаунт T-Invest с `{Moex}` подходит без правок.

**Поправка после ревью (бэктест и покрытие).** `RunNextBacktestWorkCommandHandler.PrepareAsync` считал пропуски без покрытия, так
что на `moex` каждый бэктест создавал задачу импорта, записывал каждую ночь и выходной в `gaps`, а прогресс по полной UTC-сетке не
доходил до 100 %. Теперь для рынка с сессиями из пропусков вычитается покрытие (`ISeriesCoverageRepository.GetAsync` →
`SeriesCoverage.Uncovered`) до решения об импорте и до записи `gaps`. Ожидаемое число свечей — сохранённые свечи плюс слоты
непокрытых диапазонов: на покрытой серии это ровно число сохранённых, и прогресс равен 100 %; пока покрытия нет, это полная
сетка, как раньше. Для Bybit пропуски и так лежат вне покрытия (строки нет), поэтому число не меняется, а репозиторий покрытия
не читается.

### 10. Отказ запуска бота на `moex`

`StartBotCommandHandler` первым делом после чтения бота: `bot.Instrument.Category.Exchange() == Exchange.TInvest` →
`ConflictException("Trading on MOEX is not supported yet.")`. Так ни одна проверка ключа Bybit не вызывается с аккаунтом
T-Invest.

### 11. API и фронтенд

- `POST /api/exchange-accounts` принимает `exchange` (`bybit` по умолчанию для старых клиентов), для `tinvest` — `token` и
  `accountId`. Ответы аккаунта получают `exchange`, `accountId`; `apiKey` и `uid` nullable.
- `POST /api/exchange-accounts/tinvest/accounts` `{ token }` → список счетов с доступностью и причиной; токен не
  логируется (Serilog деструктуризация запрещена для этого тела, маршрут исключён из логирования тел, как и создание).
- `GET /api/exchange-accounts/{id}/balance` отдаёт `currency` и строки; фронт подписывает валюту из ответа.
- Фронт: `AccountForm` с переключателем биржи и блоком «Показать счета»; страница и список аккаунтов по бирже; `BotForm`
  с категорией `moex` и `intervalsOf(category)`; тип категории `'spot' | 'linear' | 'moex'` в общих типах; фильтры
  «Рынок» и «Идеи» без `moex` до change 3.

### 12. Миграция и настройки

Одна forward-only миграция `AddMoexMarketMigration`: правки `exchange_accounts` (решение 2), таблица `candle_coverage`,
расширение CHECK `category` там, где он есть (сейчас — только `exchange_accounts.categories`; у остальных таблиц
колонка `category` без CHECK — проверить в задаче миграции). Настройки: секция `TInvest` во всех трёх хостах (`Endpoint`,
`Limits`, `Certificates`), `Backtests:DefaultFees:moex`. Секретов в настройках нет: токен — в базе.

**Поправка после ревью (мастер-ключ у всех Worker).** Worker без `Trading:Enabled` не требовал мастер-ключ, но импорт `moex`
расшифровывает токен аккаунта T-Invest (`TInvestTokenSource` → `ISecretProtector.Decrypt`), и такой Worker падал с
`ArgumentNullException` из `AesGcmSecretProtector`. Теперь `ConfigureWorker` требует мастер-ключ у любого Worker (как у API):
проще и честнее, раз импорт его использует. Сканер по-прежнему без ключа. Дельта спеки `background-workers` обновляет
требование «Настройки торговли».

### 13. Тесты

- Unit: лимитер (окно, пауза по трейлеру, разные токены), окна `GetCandles` и обход от конца, перевод значений и лотов,
  фильтр каталога, `SeriesCoverage.Extend`, вычитание покрытия из пропусков, маршрутизация портов, создание аккаунта по
  бирже (`ExchangeKeyRules` для Bybit, правила счёта для T-Invest), `IntervalsOf`, отказ запуска бота на `moex`.
- Integration: миграция и репозитории (`exchange_accounts` обоих видов, `candle_coverage`), API аккаунтов T-Invest на моке
  (`ITInvestCallInvokers` подменён), импорт `moex` с ночами на моке (вторая задача не делает запросов), чтение свечей
  графика в покрытии, бэктест шаблона на свечах `SBER` из фикстур с лотами и рублёвыми комиссиями.
- Frontend: формы аккаунта и бота, список интервалов по категории, страница аккаунта T-Invest.
- E2E: сценарии Bybit без изменений; стаба T-Invest нет, сценарии `moex` в e2e не добавляются.

## Risks / Trade-offs

- [Пакет `Tinkoff.InvestApi` может отставать от .NET 10 / `Grpc.Net.Client`] → версия проверяется при внедрении; при
  несовместимости — генерация клиентов из официальных proto в том же проекте (`Grpc.Tools`), контракт адаптера тот же.
- [Один токен на три процесса, лимитер на процесс] → бюджеты по хостам в сумме ниже 300/мин; пауза по трейлеру; метрики
  остатка лимита показывают, когда станет тесно; общий лимитер — отдельный change при необходимости.
- [Sub CA истекает 2027-03-06] → отпечатки и файлы в настройках и образе, замена без релиза кода; чек-лист фиксирует
  текущего издателя.
- [`FindInstrument`/`Shares` может не отдавать название или менять флаги] → каталог кэшируется 5 минут, акция — час;
  поиск по названию деградирует до поиска по тикеру.
- [Свечи выходных сессий MOEX] → считаются обычными свечами (решение разбора); если окажутся мусорными, фильтр по дню
  недели — правка адаптера.
- [Покрытие двигается, а биржа потом «дорисовала» свечу (поздняя сделка)] → read-through сохраняет только закрытые свечи с
  запасом 5 с; граница после импорта — конец последней закрытой свечи; расхождение исправляется повторным импортом
  периода вне покрытия… нет: внутри покрытия запросов нет по определению, поэтому такое исправление — ручной сброс
  строки покрытия (задача документирует SQL). Принимаем: у Bybit тот же риск был всегда.
- [Отсутствие аккаунта T-Invest ломает экраны, где раньше всё работало] → только запросы категории `moex` отвечают
  «данные недоступны»; Bybit не затронут.

## Migration Plan

1. Слить PR; API применяет миграцию при старте (как все предыдущие). Старые аккаунты получают `exchange = 'bybit'`.
2. Образы несут файлы сертификатов и секцию `TInvest` с значениями по умолчанию; `.env.prod` менять не нужно.
3. Владелец добавляет аккаунт T-Invest через UI; с этого момента `moex` читается.
4. Откат — предыдущий образ: новые колонки и таблица не мешают старому коду (`exchange` с default, nullable поля не читаются).

## Чек-лист на реальном счёте (только чтения, с разрешения владельца)

Выполняется при внедрении токеном владельца; результаты записываются сюда как поправка.

- `GetAccounts`: типы и доступ счетов; `GetInfo`: тариф.
- `Shares(BASE)`: число акций TQBR с `api_trade_available_flag`, наличие названий, `short_enabled_flag`, примеры лотов.
- `GetCandles` `SBER` `1h` за неделю с `Exchange`: что приходит ночью и в выходные; `is_complete`; `1d` за месяц — время
  открытия дневной свечи (решает судьбу `D`).
- Заголовки/трейлеры `x-ratelimit-*` на `MarketData` и `Instruments`.
- `GetTradingStatus` `SBER` в разное время дня.
- `GetOrderPrice` лимитной покупки `SBER` на 100 000 ₽: `deal_commission / initial_order_amount` → `DefaultFees:moex`.
- `GetPositions` / `GetPortfolio`: единицы (акции или лоты), `blocked`.
- Издатель сертификата сервера и срок.
- Через сколько секунд после закрытия свечи `GetCandles` отдаёт её с `is_complete = true` (запас `SeriesCoverage.Margin` = 1 мин
  должен его покрывать с запасом).
- Выравнивание свечей `120` и `240` относительно нашей UTC-сетки (`IntervalExtensions.CandleOpen`): открываются ли они в 00:00,
  02:00, 04:00 … UTC или от начала сессии MOEX.

## Open Questions

- Точная версия `Tinkoff.InvestApi` и её транзитивные зависимости (решается при внедрении; на дизайн не влияет).
- Отдаёт ли `GetLastPrices` цены по uid для всего списка TQBR одним вызовом (нужно change 3; здесь — одиночные цены).
- Нужно ли показывать на странице аккаунта T-Invest тариф из `GetInfo` (мелочь UI, решим по результатам чек-листа).
