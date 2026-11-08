# Tasks

Новая логика идёт строго по TDD: красный тест, минимальный код, рефакторинг. Ссылки «решение N» — на design.md,
сценарии — на дельты спек этого изменения. Цена и объём — `decimal`, время — UTC. Ни один тест не обращается к
реальному T-Invest: только `MockTInvestApi`.

## 1. Рынок `moex` в домене и кодах

- [x] 1.1 `MarketCategory.Moex`, `Exchange { Bybit, TInvest }` (`Entities/ExchangeAccounts/Enums`) и
  `MarketCategoryExtensions.Exchange()` (решение 1). Unit-тесты: `Spot` и `Linear` → `Bybit`, `Moex` → `TInvest`;
  `Instrument(Moex, "sber")` даёт символ `SBER`.
- [x] 1.2 `MarketDataCodes`: код `moex`, `IntervalsOf(category)` и текст ошибки интервала из списка категории (решение 1).
  Unit-тесты: `moex` разбирается и печатается; у `Moex` восемь интервалов без `D`, `W`, `M`, `360`, `720`; у `Spot` — все 13.
- [x] 1.3 `GetCandlesQueryHandler`, `CreateCandleImportCommandHandler`, `ChartCandles`, `BotRequests` проверяют интервал по
  `IntervalsOf` категории. Unit-тесты по сценариям «Дневные свечи MOEX», «Часовые свечи MOEX» (`market-data-import`),
  «Интервал не из списка категории» (`market-data-query`), «Интервал не из списка MOEX» (`bot-management`); тесты Bybit
  зелёные без правок.
- [x] 1.4 `ListedInstrument` получает `Name` и `ShortEnabled`; `InstrumentSearch.Find` сопоставляет запрос ещё и с названием
  (решение 7). Unit-тест: `сбер` находит `SBER` по названию; поиск Bybit не меняется.
- [x] 1.5 `docs/conventions.md`: категория `Moex`, `Exchange`, новые типы этого change (дополняется по ходу групп 2–5).

## 2. Аккаунт T-Invest

- [x] 2.1 `ExchangeAccount` с `Exchange`, nullable `ApiKey`/`Uid`, `AccountId` и инвариантами в конструкторе (решение 2);
  `IExchangeAccountRepository.AddAsync` отказывает по уникальному `(exchange, account_id)`. Unit-тесты инвариантов:
  аккаунт Bybit без ключа и аккаунт T-Invest без счёта не создаются.
- [x] 2.2 Миграция `AddMoexMarketMigration`: `exchange` с заполнением `'bybit'`, nullable `api_key`/`uid`, `account_id`,
  CHECK категорий с `moex`, частичные уникальные индексы, таблица `candle_coverage` (решения 2, 4, 12). Интеграционный
  тест миграции: старые строки получили `exchange = 'bybit'`, второй аккаунт того же `account_id` отклоняется базой,
  строка покрытия вставляется и читается.
- [x] 2.3 `ExchangeAccountRepository` и `ExchangeAccountDb` с новыми колонками. Интеграционные тесты: аккаунт T-Invest
  сохраняется с зашифрованным токеном и читается без него (сценарий «Токен в базе зашифрован»); `GetSecretAsync`
  расшифровывается `AesGcmSecretProtector` в тот же токен; уникальность счёта (сценарий «Второй токен того же счёта»).
- [x] 2.4 Порт `Ports/ExchangeAccounts/IBrokerAccountReader` (`ListAccountsAsync(token)` → `BrokerAccount` с номером,
  названием, типом, статусом, доступом и причиной недоступности; `GetPortfolioAsync(token, accountId)`) и валюта в
  `AccountBalance` (решение 2). Unit-тест чистой проверки «можно сохранить»: открытый брокерский с `FullAccess` — да,
  закрытый ИИС — `not_open`, чтение — `not_full_access`.
- [x] 2.5 `CreateExchangeAccountCommand` с `Exchange`, `Token`, `AccountId`; обработчик по бирже: Bybit как сейчас, T-Invest —
  повторное чтение счетов и проверка выбранного (решение 2). Unit-тесты по сценариям «Валидный аккаунт T-Invest»,
  «Доступ отозван между выбором и сохранением», «T-Invest не отвечает при создании», пустые поля — ошибки по полям, счета
  не читаются; сценарии Bybit зелёные.
- [x] 2.6 Запрос `ListBrokerAccountsQuery(token)` и `GetExchangeAccountBalance` по бирже (Bybit — инспектор, T-Invest —
  `IBrokerAccountReader`, валюта `RUB`). Unit-тесты по сценариям «Два счёта токена», «Токен отклонён», «Портфель со
  стоимостью».
- [x] 2.7 Контракты и контроллер: `exchange` в `ExchangeAccountBody` (`bybit` по умолчанию), `token`, `accountId`;
  `exchange`/`accountId` в ответах; `POST /api/exchange-accounts/tinvest/accounts`; `currency` в балансе; тело с токеном
  не логируется (решение 11). Интеграционные тесты API на моке: создание аккаунта T-Invest возвращает id и ответ без
  токена; старое тело без `exchange` создаёт аккаунт Bybit; список счетов токена.

## 3. Адаптер T-Invest

- [x] 3.1 Пакет `Tinkoff.InvestApi` в `Directory.Packages.props` и `ProfitFox.Infrastructure.csproj`; `TInvestSettings` с
  валидацией endpoint (официальный хост или `.invalid`), секция `TInvest` в `appsettings.json` трёх хостов с бюджетами
  `MarketData` API 100 / Worker 150 / Scanner 50 (решения 3, 7). `dotnet build` зелёный; unit-тест: чужой хост не проходит
  валидацию.
- [x] 3.2 `Tests.Shared/TInvest/MockTInvestApi` (`CallInvoker` с обработчиками по методу, запись вызовов, метаданных и
  трейлеров, `RpcException` по требованию) и `ITInvestCallInvokers` в адаптере (решения 6, 7). Unit-тест мока: вызов
  зарегистрированного метода отдаёт ответ и записывает `authorization` и `x-app-name`.
- [x] 3.3 `TInvestValues` (решение 7). Unit-тесты: `Quotation(305, 500000000)` → `305.5`; обратный перевод без `double`;
  отрицательные нано; `Timestamp` → UTC.
- [x] 3.4 `TInvestRequestLimiter` (решение 7). Unit-тесты на `FakeTimeProvider`: лимит группы в окне минуты; разные
  токены не делят окно; `Pause` держит группу до сброса и не укорачивается более ранней паузой; ожидание до отправки.
- [x] 3.5 `TInvestCall`: лимитер → метаданные → дедлайн → метрики → перевод `RpcException` (решение 7). Unit-тесты на моке:
  `Unauthenticated` → `ExchangeKeyRejectedException`; `ResourceExhausted` с трейлером `x-ratelimit-reset: 7` →
  `ExchangeRateLimitedException` и пауза группы на 7 с; `Unavailable` → `ExchangeUnavailableException`; описание статуса
  `30220` попадает в `ExchangeException` кодом, прочий текст брокера — нет.
- [x] 3.6 `TInvestMetrics` и регистрация meter в `MetricsRegistrar` (решение 7). Unit-тесты через `MetricCollector` по
  сценариям «Чтение свечей», «Лимит токена», «Остаток группы market_data»; вызов, не выпущенный паузой, не считается.
- [x] 3.7 `TInvestChannel` с проверкой цепочки до закреплённого корня через Sub CA по отпечаткам из настроек; файлы `.cer`
  копируются в вывод (решение 7). Unit-тесты: цепочка из тестовых самоподписанных сертификатов с верными отпечатками
  проходит, с чужим отпечатком или несовпадающим именем — нет; файл с другим отпечатком не загружается.
- [x] 3.8 `TInvestTokenSource` (решение 3). Unit-тесты: без аккаунта T-Invest — `ExchangeUnavailableException` «данные
  T-Invest недоступны» и ни одного вызова мока; с аккаунтом — расшифрованный токен; повторное чтение в пределах 5 минут
  не идёт в репозиторий; через 5 минут после удаления аккаунта источник пуст.
- [x] 3.9 `TInvestShares`: `ShareBy(ticker, TQBR)` с кэшем час, фильтр рубли / API-торговля / лот и шаг цены (решение 7).
  Unit-тесты на моке: акция разрешается один раз за час; акция другой валюты или без API-торговли — `ExchangeException`.
- [x] 3.10 `TInvestMarketDataClient`: каталог (`Shares(BASE)` с фильтром TQBR), `InstrumentExistsAsync`, правила инструмента,
  `GetLastPriceAsync`/`GetLastPricesAsync` (решение 7). Unit-тесты на моке по сценариям «Акции MOEX в поиске», «Акция не
  TQBR», «Правила акции»; `ListedInstrument` несёт `TradFi`, `Name`, `ShortEnabled`, лот как шаг и минимум.
- [x] 3.11 История: окна по интервалу, обход от конца периода, источник `Exchange` без `limit`, только `is_complete`, объём ×
  лот, оборот по типичной цене (решение 7). Unit-тесты на моке по сценарию «Лоты в акции»; неделя часовых свечей идёт
  одним окном, три месяца — двумя; `GetCandlesOnceAsync` одной попыткой; незакрытая свеча отброшена; свечи новейшие первыми.
- [x] 3.12 `TInvestAccountClient : IBrokerAccountReader`: `GetAccounts`, `GetPortfolio` в рублях (решение 7). Unit-тесты на
  моке: счета с типами и доступом; портфель `50 000` ₽ + `10` `SBER` по `300` → строки и общая стоимость `53 000`.

## 4. Маршрутизация и покрытие

- [x] 4.1 `RoutedMarketHistoryClient`, `RoutedInstrumentRulesProvider`, `RoutedInstrumentCatalog` и регистрация в
  `InfrastructureRegistrar` (решение 8). Unit-тесты: `Spot`/`Linear` уходят в Bybit, `Moex` — в T-Invest, для каждого
  метода порта; `CachedInstrumentCatalog` кэширует `Moex` отдельно.
- [x] 4.2 `SeriesCoverage(From, Until)` с `Extend(from, to)` и вычитание покрытия из пропусков
  (`Interval.MissingRanges` → `SeriesCoverage.Uncovered(ranges)`) (решение 4). Unit-тесты: касающийся и пересекающий
  диапазоны расширяют границы, отдельный — нет; пропуск внутри покрытия исчезает, частично снаружи — обрезается.
- [x] 4.3 `ISeriesCoverageRepository` и `SeriesCoverageRepository` над `candle_coverage`. Интеграционные тесты: upsert и
  чтение, отсутствующая серия — `null`.
- [x] 4.4 `CandleReadThrough` по покрытию: пропуски минус покрытие, граница двигается в одной транзакции с записью свечей до
  `min(to, конец последней закрытой свечи)` (решение 4). Unit-тесты по сценариям «Ночь MOEX не догружается», «Пропуск в
  истории Bybit» (без строки покрытия поведение прежнее), «Граница не заходит в будущее».
- [x] 4.5 `RunNextCandleImportCommandHandler` по покрытию (решение 4). Интеграционные тесты на моке по сценариям «Ночь MOEX
  внутри покрытия» (вторая задача `Succeeded` без вызовов мока), «Период за границей» (запрошен только хвост); импорт Bybit
  на `FakeBybit` без изменений поведения.
- [x] 4.6 Проверка символа импорта и бота у биржи категории (сценарии «Акция не TQBR», «Акция MOEX»). Unit-тесты обработчиков
  с маршрутизатором на моках обеих бирж.

## 5. Бэктест, бот и запуск

- [x] 5.1 `BacktestSettings`: `DefaultFees` обязаны содержать `Limit` и `Market` всех категорий (`ValidateOnStart`),
  `moex` в `appsettings` трёх хостов `0.05` / `0.05` / `0.1` (решение 9). Unit-тесты: без `moex` валидация падает с
  понятным текстом; `Policy()` отдаёт ставки `Moex`; `HasIdeaSlippage` по-прежнему про `spot` и `linear`.
- [x] 5.2 Бэктест на акциях: интеграционный тест по сценариям «Покупка на сумму в лотах», «Ордер через ночь», «Ставки MOEX
  по умолчанию» на фикстуре свечей `SBER` 60 с ночными дырами (`CandleFixtures`) и правилах лот `10` / шаг `0.01`;
  результат детерминирован.
- [x] 5.3 `BotRequests`: шорт на `Moex` отклоняется как на `Spot` (сценарий «Шорт на MOEX»); аккаунт Bybit у бота на `moex`
  отклоняется по категориям (сценарий «Аккаунт Bybit у бота на MOEX»). Unit-тесты.
- [x] 5.4 `StartBotCommandHandler`: отказ для `TInvest` до обращений к бирже (решение 10). Unit-тест по сценарию «Бот на
  MOEX»: `ConflictException` с причиной, инспектор ключа не вызван.

## 6. Фронтенд

- [x] 6.1 Типы: `exchange` и `accountId` в аккаунте, `currency` в балансе, категория `'spot' | 'linear' | 'moex'`,
  `intervalsOf(category)` в `modules/market-data` (решение 11). Unit-тесты `intervals.test.ts`: `moex` без `D`.
- [x] 6.2 `AccountForm` с переключателем биржи, полями токена и счёта, действием «Показать счета» и сбросом токена после
  отказа (сценарии «Счёт выбран и сохранён», «Недоступный счёт», «Смена биржи в форме», «Ключ отклонён»). Тесты формы
  с замоканным API.
- [x] 6.3 Страница и список аккаунтов по бирже: номер счёта, маска токена, категория `moex`, баланс в валюте ответа
  (сценарии «Баланс в рублях», «Аккаунт T-Invest в списке»). Тесты страниц.
- [x] 6.4 `BotForm`: категория `moex`, интервалы по категории со сбросом, подсказки акций, подпись `RUB` (сценарии
  «Интервалы MOEX», «Подсказки акций», «Рубли»). Тесты формы.
- [x] 6.5 `npm run lint`, `format:check`, `typecheck`, `test`, `build` зелёные; e2e Bybit без изменений.

## 7. Сборка и чек-лист

- [x] 7.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` зелёные; `OutboundHttpGuard` не фиксирует ни
  одного запроса к хосту T-Invest из тестов.
- [ ] 7.2 Чек-лист на реальном счёте (design.md, с разрешения владельца, только чтения): результаты записаны поправкой в
  design.md, `DefaultFees:moex` и решение по `D` обновлены по ним.
- [ ] 7.3 Ручная проверка в dev: аккаунт T-Invest добавлен через UI, поиск `SBER`, график бота `moex` / `SBER` / `60`
  открывается, повторное открытие не даёт запросов к T-Invest в метриках, бэктест шаблона на `SBER` считается.

## 8. Исправления по ревью

Находки ревью PR #83; каждое исправление — красный тест, минимальный код, рефакторинг. Поправки решений — в design.md
(«Поправка после ревью»), конвенции — в `docs/conventions.md`.

- [x] 8.1 Покрытие растёт на весь проверенный период: read-through — `[from, min(to, граница))`, импорт — нарастающим
  `[job.From, конец диапазона)` после каждого диапазона (решение 4). Unit-тесты: импорт старого периода, касающегося покрытия
  через сохранённые дни, склеивает покрытие; read-through периода с сохранёнными свечами между пропусками двигает границу на
  весь период; прогресс покрытия сохраняется после каждого диапазона.
- [x] 8.2 Бэктест на `moex` вычитает покрытие из пропусков до решения об импорте и до записи `gaps`; `expected` — сохранённые свечи
  плюс слоты непокрытых диапазонов (решение 9, поправка). Unit-тест `PrepareAsync`: покрытая серия с ночами — импорт не
  создаётся, `gaps` пустые, загружено 100 %; интеграционный тест `MoexBacktestTests` проверяет `gaps`.
- [x] 8.3 Запас границы `SeriesCoverage.Margin` = 1 мин в read-through и импорте: `border = min(to, CandleOpen(now − Margin))`;
  Bybit без изменений. Unit-тесты обоих обработчиков; чек-лист 7.2 дополнен пунктами про `is_complete` и выравнивание 120/240.
- [x] 8.4 Свой `ExchangeResilienceExecutor` на биржу (keyed по `Exchange`): открытая цепь T-Invest не трогает вызовы Bybit. Unit-тест.
- [x] 8.5 Живая свеча бота на `moex`: `FormingCandleFeeds` отдаёт `NoData` без подписки на Bybit для инструмента T-Invest. Unit-тесты.
- [x] 8.6 Отмена gRPC-вызова: `ThrowOperationCanceledOnCancellation` в канале, `Cancelled` при отменённом токене —
  `OperationCanceledException` без учёта в метриках. Unit-тест на моке.
- [ ] 8.7 Фронтенд: `chartIntervals(own, category)` и `shownInterval(asked, own, category)` через `intervalsOf(category)`; у бота
  `moex` нет `D`. Тесты `chartIntervals.test.ts` и страницы бота.
- [x] 8.8 Мастер-ключ обязателен у всех Worker (решение 12, поправка): импорт `moex` расшифровывает токен. Тест регистрации.
- [x] 8.9 Аккаунт не Bybit отклоняется в настройках торговли по идеям, в автомате и в `IdeaTradeEntryCalculator.AccessAsync` до
  `KeyAsync`. Unit-тесты.
- [x] 8.10 Портфель T-Invest: каждая валютная позиция отдельной строкой, стоимость в рублях, итог из `TotalAmountPortfolio`.
  Unit-тест с RUB и USD.
- [x] 8.11 Лимитер: `Limits` > 0 в `TInvestSettings.Problem()`; ключ словарей — SHA-256-отпечаток токена (решение 7);
  `Waiting_request_is_cancelled_with_its_token` ждёт `Should.ThrowAsync`. Unit-тесты.
- [x] 8.12 `TInvestTokenSource`: кэш — ссылочный `sealed record` через `Volatile.Read`/`Volatile.Write`.
- [ ] 8.13 Пиннинг: второй элемент цепочки — закреплённый Sub CA, `DisableCertificateDownloads`, OID serverAuth в
  `ApplicationPolicy`. Unit-тест: сервер другого Sub CA под тем же корнем отклоняется.
- [ ] 8.14 Фронтенд `AccountForm`: переключатель биржи и токен заблокированы на время запросов, устаревший ответ игнорируется,
  смена токена сбрасывает счета; `not_open` — «не открыт»; «Ключ / счёт» в списке; «биржа не отвечает» в графике и форме
  бэктеста. Тесты формы и страниц.
- [ ] 8.15 Интеграционные тесты журнала: токен не попадает в лог при `POST /api/exchange-accounts/tinvest/accounts`, принятом и
  отклонённом.

## Workflow follow-up

- Архивировать change после ревью владельца и открыть PR `feat/add-moex-market`.
- Следующие change: `add-moex-live-bots` и `add-moex-ideas` параллельно, затем `add-moex-idea-trades`.
