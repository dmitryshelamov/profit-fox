# Tasks

Каждая задача с новой логикой идёт red → green → refactor: сначала только тест, запуск и падение по ожидаемой причине,
затем минимальный код, затем правка структуры при зелёных тестах. Кода, которого не требует ни один тест, нет: нужен
новый случай — сначала падающий тест. Цены, объёмы, обороты, открытый интерес и ставки — `decimal` на сервере и
десятичные строки в API; время — UTC `DateTimeOffset`, текущий момент — только через `TimeProvider`. Ни один тест и
локальный запуск не обращается к настоящему Bybit: REST — `FakeBybitHandler`, потоки — `FakeBybitSocketServer`, e2e —
заглушка.

## 1. Проект сканера и аренда

- [x] 1.1 Проект `backend/src/ProfitFox.Scanner` (generic host, `Program.Configure` с Serilog и
  `InfrastructureRegistrar.ConfigureScanner`, `appsettings.json`, `appsettings.Development.json` с `Scanner:Symbols` из
  трёх символов, `appsettings.Testing.json`, Dockerfile по образцу Worker'а) в `ProfitFox.sln` — `dotnet build
  backend/ProfitFox.sln` зелёный, `docker build -f src/ProfitFox.Scanner/Dockerfile backend` собирает образ
- [x] 1.2 `ScannerSettings` (секция `Scanner`: `Symbols`, `HistoryDays`, `RestRequestsPerSecond`, `RestPause`,
  `UniverseRefresh`, `SnapshotDelay`, `LeaseDuration`, `LeaseRenewal`, `TopicsPerConnection`) и проверка в
  `ConfigureScanner`: продление не короче аренды — сканер не стартует — integration-тест на хосте с такими настройками
- [x] 1.3 Миграция `scanner_state` (одна строка `id = 1`: `lease_owner`, `leased_until`, `snapshot_at`, `pairs_total`,
  `pairs_backfilled`, `rest_paused_until`) и репозиторий аренды — integration-тесты на Postgres: свободную аренду берёт
  первый; занятую второй не берёт; истёкшую берёт другой; продление чужой аренды не проходит
- [x] 1.4 `ScannerHost` в `Tests.Integration/Infrastructure` по образцу `WorkerHost` (общая база, `FakeBybitHandler`,
  `FakeBybitSocketServer`, фейковые часы, проверка разрешения всех сервисов) — integration-тест: два `ScannerHost` на
  одной базе, поток держит один, второй не открывает соединений с заглушкой; держатель остановлен — после истечения
  аренды поток берёт второй (спека «Один держатель потока»)

## 2. Список пар

- [x] 2.1 `ListedInstrument` получает `Trading` и `Perpetual`; `BybitMarketDataClient.ListAsync` заполняет их из статуса
  и типа контракта Bybit — integration-тесты `BybitInstrumentCatalogTests` на `FakeBybitHandler`: `Trading` /
  не `Trading`, `LinearPerpetual` / фьючерс с датой, спот всегда не `Perpetual`; тесты поиска инструментов проходят без
  правок
- [x] 2.2 Миграция `scanner_pairs` (`category`, `symbol`, `listed`, `history_from`, `covered_until`, `oi_covered_until`,
  `funding_covered_until`) и репозиторий — integration-тесты на Postgres: запись, чтение, отметка `listed = false`
- [x] 2.3 `Scanner/Universe` в Application — unit-тесты: берутся `spot` USDT `Trading` и `linear` USDT `Trading`
  `Perpetual`; `ETHBTC`, USDC-перпетуал и фьючерс с датой отброшены; `Scanner:Symbols` сужает список; новая пара
  добавляется, пропавшая получает `listed = false`; перечитывание не чаще `UniverseRefresh` (спека «Список пар
  сканера»)

## 3. REST сканера: тикеры, открытый интерес, фандинг, лимиты

- [x] 3.1 `ExchangeRateLimitedException` (подкласс `ExchangeUnavailableException`); `Unwrap` бросает его на 429,
  `10006` и 403 — unit- и integration-тесты на `FakeBybitHandler`: каждый из трёх ответов даёт новое исключение; задачи
  загрузки и график бота на него ведут себя как на недоступность (их тесты проходят без правок)
- [x] 3.2 Порт `IMarketDataFeed` и `BybitPublicDataClient`: `GetTickersAsync(category)`, `GetOpenInterestAsync(symbol,
  from, to)` (шаг `5min`, страницы по 200, значения в монетах), `GetFundingHistoryAsync(symbol, from, to)` — одна
  попытка на запрос — integration-тесты на `FakeBybitHandler`: значения разобраны точно, страницы пройдены курсором,
  у `spot` нет открытого интереса и фандинга
- [x] 3.3 `ScannerRestGate`: токен-бакет `RestRequestsPerSecond` на все REST-запросы сканера; на
  `ExchangeRateLimitedException` — пауза `RestPause` в `scanner_state.rest_paused_until`, до её конца ни одного
  запроса — unit-тесты с `FakeTimeProvider`: частота не выше настройки; после 403 запросов нет до конца паузы, потом
  идут; прочая временная ошибка повторяется лестницей `ExchangeResilienceExecutor` (спека «Лимиты Bybit»)

## 4. Хранилище открытого интереса и фандинга

- [x] 4.1 Миграция `open_interest (category, symbol, time, value)` — гипертаблица по `time` — и `funding_rates
  (category, symbol, time, rate)`; репозитории с идемпотентной записью и чтением за период — integration-тесты на
  Postgres: повторная запись не создаёт второй строки, значения хранятся точно, чтение `[from, to)` по времени

## 5. Метрики

- [x] 5.1 Эталонные окна минут, точек открытого интереса и тикеров с заранее посчитанными метриками в
  `Tests.Shared/Fixtures/Market` — файлы на месте, значения посчитаны вручную и записаны в тестах явно
- [x] 5.2 `Domain/Services/Market/MarketMetrics` — unit-тесты на эталонных окнах: изменение за 5, 15, 60, 240 и
  1440 минут; оборот за 60 и 1440 минут; спред; открытый интерес в USDT (монеты × цена) и его изменение за 1 и
  24 часа; у `spot` нет открытого интереса и фандинга (спека «Метрики снимка»)
- [x] 5.3 Пустота и минуты без сделок — unit-тесты: окно без всех минут даёт пустую метрику, а не посчитанную по
  короткому окну; минута без сделок внутри покрытого диапазона считается нулевым оборотом и close предыдущей минуты
  (спека «Метрика без полного окна пуста»)

## 6. Поток минутных свечей пачками

- [x] 6.1 Сверить с документацией Bybit лимиты публичного потока: топики на запрос подписки и на соединение, новые
  соединения на IP; записать найденное в `design.md` (решение 5) и умолчание `TopicsPerConnection` — документ обновлён
- [x] 6.2 Порт `IMarketStreams.SubscribeMinuteCandlesAsync(instruments, closed, restored, ct)` и `BybitMarketStreams`
  на своём `BybitSocketClient` — integration-тесты на `FakeBybitSocketServer`: 250 символов уходят пачками не больше
  лимита в соединениях не больше `TopicsPerConnection`; наружу идут только закрытые свечи; обрыв соединения даёт
  `restored`; `Dispose` снимает подписки; тесты `BybitStreams` и живой торговли проходят без правок

## 7. Загрузка истории и покрытие

- [x] 7.1 `Scanner/Backfill` в Application — unit-тесты на моках портов: сначала последние 24 часа минут и открытого
  интереса всех пар, затем остальное окно `HistoryDays` пара за парой от новых к старым, затем фандинг `linear`;
  `history_from` и `pairs_backfilled` пишутся, когда пара загружена целиком; пара моложе окна начинается с первой
  свечи; повторный запуск не запрашивает загруженное (спека «История за 90 дней»)
- [x] 7.2 Граница покрытия — unit-тесты: подряд пришедшие минуты двигают `covered_until`; минута с разрывом не двигает;
  `restored` и старт догружают `[covered_until, now)`; минуты без сделок внутри покрытого диапазона не
  запрашиваются повторно и не пишутся в `candles`; пара с отстающим покрытием неполная (спека «Пропуск после
  перерыва»)
- [x] 7.3 Свечи истории и потока пишутся в `candles` с интервалом `1` — integration-тест на `ScannerHost`: после загрузки
  в `candles` минуты всех пар окна, незакрытой свечи нет, повтор не дублирует строки

## 8. Снимок и цикл сканера

- [x] 8.1 Миграция `market_snapshot` (строка на пару: `category`, `symbol`, `computed_at`, метрики, `incomplete`) и
  репозиторий замены снимка одной транзакцией с проверкой аренды — integration-тесты на Postgres: снимок заменяется
  целиком; держатель, потерявший аренду, снимок не пишет
- [x] 8.2 Минутный цикл (`Scanner/Minute` в Application): тикеры каждой категории раз в минуту; точка открытого интереса
  на начало 5 минут при первом чтении после границы; через `SnapshotDelay` — метрики всех пар и замена снимка;
  `snapshot_at` в состоянии; фандинг дочитывается раз в час — unit-тесты с `FakeTimeProvider` (спеки «Тикеры раз в
  минуту», «Снимок рынка», «Состояние сканера»)
- [x] 8.3 `ScannerBackgroundService` связывает аренду, список пар, поток, загрузку и минутный цикл; сбой одного шага не
  останавливает хост — integration-тест на `ScannerHost` с заглушкой: после старта на пустой базе появляются снимок и
  состояние с ходом загрузки, после закрытия минуты в заглушке — новая свеча и новый `snapshot_at`
- [x] 8.4 Падение сканера не трогает ботов — integration-тест: торгующий `WorkerHost` ведёт бота на `FakeExchange`,
  `ScannerHost` останавливается с ошибкой, бот продолжает торговать, его статус и ордера не меняются (спека «Сканер —
  отдельный процесс»)

## 9. API рынка

- [x] 9.1 `GetMarketQuery` и `GET /api/market`, `GET /api/market/{category}/{symbol}` в `MarketController`, контракты в
  `Contracts/Market`, `[ProducesResponseType]` — integration-тесты `MarketApiTests` на Postgres: снимок и состояние в
  ответе десятичными строками, пустые метрики — `null`; без снимка — пустой список; пары нет — 404; неизвестная
  категория — 400; Bybit не вызывается (спеки «Чтение снимка рынка», «Строка пары»)
- [x] 9.2 Общая часть проверки, выравнивания и обрезки периода свечей выносится из `GetBotCandlesQueryHandler`;
  `GetMarketCandlesQuery` и `GET /api/market/{category}/{symbol}/candles` — unit- и integration-тесты: те же правила, что
  у свечей бота (ошибки по параметру, 1000 свечей, обрезка, `unavailable`); тесты свечей бота проходят без правок
  (спека «Свечи пары»)
- [x] 9.3 `GET /api/market/linear/{symbol}/open-interest` и `/funding` — integration-тесты: точки периода из хранилища,
  больше 1000 точек — 400 по `from`, `spot` — 404, Bybit не вызывается (спека «Открытый интерес и фандинг пары»)
- [x] 9.4 Запустить API и `npm run generate:api`, закоммитить `generated/schema.d.ts` с контрактами — в схеме есть
  `MarketResponse` и `MarketRowResponse`, `npm run typecheck` зелёный

## 10. Экраны

- [x] 10.1 `modules/market/model` — Vitest red → green: фильтр по категории со счётчиками, поиск по части символа,
  сортировка в обе стороны с пустыми в конце, возраст снимка и порог 3 минуты, компактные числа («12.4 млн»), знак
  фандинга
- [x] 10.2 `modules/market/api.ts` (снимок с опросом раз в 15 секунд, строка пары, открытый интерес, фандинг) и
  `pages/market` с `MarketTable`, баннерами загрузки, паузы, устаревания и «ещё не собрал», значком неполных данных;
  пункт «Рынок» в меню; маршрут `/market` — тесты экрана через `renderApp` и `fakeApi`: колонки и «—» у `spot`;
  сортировка и фильтр сохраняются после обновления; каждый баннер на своём состоянии (спеки «Раздел „Рынок“»,
  «Фильтр, поиск и сортировка», «Обновление таблицы», «Загрузка истории на экране», «Пауза REST на экране»,
  «Устаревшие данные», «Неполные данные пары»)
- [x] 10.3 Пагинация свечей бота переезжает в `market-chart` хуком над функцией чтения страницы — тесты `useBotCandles`
  и графика бота проходят без правок смысла
- [x] 10.4 Графики открытого интереса и фандинга в `market-chart` и `pages/market-pair` (`/market/$category/$symbol`,
  `?interval=`, по умолчанию 15 мин) — тесты экрана: у `linear` метрики, свечи и оба графика; у `spot` без них;
  неизвестная пара — сообщение и ссылка на «Рынок» (спека «Страница пары»)

## 11. e2e

- [x] 11.1 Заглушка Bybit: статусы и типы контрактов в `instruments-info`, `/v5/market/tickers`,
  `/v5/market/open-interest`, `/v5/market/funding/history`, пачечная подписка `kline.1` — `npm run test:e2e` для
  существующих сценариев зелёный
- [x] 11.2 Playwright запускает сканер с `Scanner:Symbols` из трёх пар и `Scanner:HistoryDays = 1`; сценарий
  `market.spec.ts`: «Рынок» показывает три пары, сортировка по изменению за 1 ч меняет порядок, строка открывает
  страницу пары с графиком — `npm run test:e2e` зелёный

## 12. Документы и общая проверка

- [x] 12.1 `CONTEXT.md`: термины «Сканер», «Снимок рынка», «Открытый интерес», «Фандинг»; `docs/conventions.md`: раздел
  `ProfitFox.Scanner`, папки `Scanner`, `MarketController`, `modules/market`, секция настроек `Scanner`; комментарий в
  Dockerfile сканера, как запускать его на сервере — документы обновлены
- [x] 12.2 Пройти дифф: каждая ветка, условие и проверка нового кода требуются каким-то тестом — лишнее удалено или
  покрыто тестом
- [x] 12.3 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` (и с `TZ=Asia/Tokyo`),
  `dotnet format backend/ProfitFox.sln --verify-no-changes`, во frontend `npm run lint`, `npm run format:check`,
  `npm run typecheck`, `npm test`, `npm run build`, `npm run test:e2e` — всё зелёное

## Workflow follow-up

- Ревью диффа перед архивом (урок #26): сканер не трогает торговлю, путь REST не повторяет 429/403.
- `/opsx:archive`, затем PR `feat(market): add market scanner` со squash.
- Выкатка на `soyo-01`: сервис сканера в `docker-compose.prod.yml` — только с отдельного разрешения владельца; затем
  смотреть ход загрузки истории и отсутствие 429/403.
