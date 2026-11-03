# Tasks

Каждая задача с новой логикой идёт red → green → refactor: сначала падающий тест, затем минимальный код.

## 1. Убрать testnet

- [x] 1.1 Domain/Application: убрать `ExchangeEnvironment` из `ExchangeAccount`, команд и `ExchangeAccountCodes`; UID
  уникален без окружения — unit- и integration-тесты аккаунтов по дельте `exchange-accounts` зелёные
- [x] 1.2 Миграция: удалить ботов-ссылки на testnet-аккаунты, сами аккаунты, колонку `environment`, `CHECK`, заменить
  `UQ(uid, environment)` на `UQ(uid)` — integration-тест миграции на базе с testnet-аккаунтом и ботом
- [x] 1.3 Infrastructure: `BybitAccountClient` всегда основная сеть (или `Bybit:RestAddress`) — тест адреса в
  `BybitAddressTests`
- [x] 1.4 Presentation: убрать `environment` из `ExchangeAccountBody`, `ExchangeAccountResponse`,
  `ExchangeAccountDetailsResponse`, `BotAccountResponse` — integration-тесты API аккаунтов и ботов
- [x] 1.5 Frontend: перегенерировать `schema.d.ts`, удалить `EnvironmentBadge` и выбор окружения в `AccountForm`,
  «· Testnet» в `BotForm` — Vitest аккаунтов и ботов по дельтам интерфейсов, e2e `accounts` зелёный

## 2. Единица работы

- [x] 2.1 `ITransactionExecutor` (Application/Transactions) и `PostgresTransactionAccessor` /
  `PostgresTransactionExecutor` (Infrastructure) с `EnsureNoTransaction` — integration-тесты: коммит видим, исключение
  откатывает всё, вложенный вызов запрещён
- [x] 2.2 Записать единицу работы в `docs/conventions.md` (кто её использует, что существующие репозитории не
  переведены) — документ обновлён

## 3. Исполнение стратегии

- [x] 3.1 `HoldLimit`: ордер с исполнением не отменяется из-за размера, только из-за цены — unit-тесты сценариев
  «Частично исполненная покупка», «Частично исполненная продажа», «Новая цена после частичного исполнения»; эталонные
  тесты бэктеста и `EngineVersion.Current = 2` без изменений
- [x] 3.2 `RunSnapshot` и `StrategyRun.Restore`: старт с позицией, остатком, активными ордерами по `NodePath`, деньгами
  и `lastOrderNumber` — unit-тесты «Лимитка после рестарта», «Номер позиции» и продолжение номеров ордеров
- [x] 3.3 Вывод денег прогона из баланса бота и сохранённых позиций (лонг и шорт) — unit-тест: снимок после серии
  исполнений даёт те же `RunState`, что непрерывный прогон
- [x] 3.4 `StrategyRun.WarmUp`: граф без узлов ордеров, позиция не меняется — unit-тест «EMA после запуска»
- [x] 3.5 `StrategyRun.Record`: исполнения и отмены без вычисления узлов, правила позиции работают — unit-тесты
  «Исполнение на паузе», «Выход на паузе»

## 4. Домен живой торговли

- [x] 4.1 `LiveBot` и `LiveBotStatus`: переходы `start`, `pause`, `resume`, `stop`, `close-position`, `Stopping` →
  `Stopped`/`Failed`, `start_pending` — unit-тесты каждого перехода и каждого конфликта из спеков
- [x] 4.2 Идентификатор клиента: `{bot id base36}-{номер}`, ≤ 36 символов, разбор обратно в бота и номер, чужой формат
  не разбирается — unit-тесты
- [x] 4.3 Сверка: `linear` со знаком, `spot` по монете «не меньше», ордера сохранённые против биржи, неизвестный ордер
  бота — unit-тесты сценариев «Сверка с биржей» и «Расхождение при сверке»
- [x] 4.4 Проверка денег при запуске — unit-тест «Денег не хватает с учётом другого бота»

## 5. Хранение живой торговли

- [x] 5.1 Миграция `live_bots`, `live_positions`, `live_orders`, `live_fills` с ограничениями из решения 5 —
  integration-тест миграции
- [x] 5.2 Репозиторий живого бота: чтение, запись в транзакции, захват под аренду с `lease_epoch`, продление,
  проверка аренды `FOR UPDATE` — integration-тесты: два Worker'а не берут одного бота, перехват увеличивает номер,
  запись со старым номером откатывается
- [x] 5.3 Репозиторий позиций, ордеров и исполнений: повтор `exec_id` ничего не меняет, выборка `Pending*` по
  порядку номера — integration-тесты «Повтор исполнения» и порядок отправки

## 6. Управление ботом

- [x] 6.1 Правка и удаление бота под блокировкой строки `bots` с проверкой `live_bots`: конфликт у работающего бота,
  ошибка поля для аккаунта, инструмента и интервала при открытой позиции, удаление с позициями — integration-тесты
  дельты `bot-management`, включая гонку запуска и правки
- [x] 6.2 Статус и причина в чтении бота и в списке ботов — integration-тест «Новый бот»

## 7. Команды и API

- [x] 7.1 Порт `IExchangeLedger` для API-проверок запуска (ключ, режим позиций, свободные деньги) и
  `StartBotCommand` — integration-тесты «Бот без аккаунта», «Денег не хватает…», hedge-режим, ключ отозван (`409`),
  Bybit недоступен (`503`), «Повторный запуск» (`409`)
- [x] 7.2 `PauseBotCommand`, `ResumeBotCommand`, `StopBotCommand`, `ClosePositionCommand` с конфликтами по статусу —
  integration-тесты API
- [x] 7.3 `GET /api/bots/{id}/positions` — integration-тест «Позиции после закрытия»
- [x] 7.4 `BotsController`: эндпоинты действий с `[ProducesResponseType]`, Swagger описывает их — тест OpenAPI-документа

## 8. Адаптеры Bybit

- [x] 8.1 `IExchangeLedger` на REST с ключом аккаунта: открытые ордера, история ордеров и исполнений по
  `orderLinkId`, позиции, баланс, режим позиций, последние 1000 закрытых свечей — тесты на `FakeBybitHandler`
- [x] 8.2 `IOrderGateway`: отправка лимитного и рыночного (spot по сумме — `marketUnit=quoteCoin`, linear — объём по
  цене), отмена, классификация отказов в `BybitCodes`, повтор `orderLinkId` как принятие — тесты на `FakeBybitHandler`
  по каждому коду
- [x] 8.3 `FakeBybitSocketServer` в `Tests.Shared` и `IExchangeStreams` на `IBybitSocketClient`: kline только с
  `confirm`, `order`, `execution`, сигнал восстановления после обрыва — тесты адаптера на заглушке

## 9. Рантайм в Worker'е

- [x] 9.1 Секция `Trading`: торговля выключена по умолчанию, при включённой — проверка мастер-ключа; ключ разработки в
  `appsettings.Development.json` и `appsettings.Testing.json` Worker'а — integration-тесты «Торговля без
  мастер-ключа», «Worker только для бэктестов»
- [x] 9.2 `LiveTradingBackgroundService` и `RunNextLiveBotCommand`: захват ботов, продление аренды, выгрузка при
  потере — integration-тесты «Два торгующих Worker'а», «Аренда перехвачена», «Worker без торговли»
- [x] 9.3 `LiveBotSession`: очередь событий, транзакция события, `RunStarted` только при `start_pending` —
  integration-тесты «Запуск остановленного бота», «Рестарт бота DCA», «Два бота на одном символе», «Чужой ордер»
- [x] 9.4 `LiveOrderSender`: отправка `PendingSubmit` и `PendingCancel` по порядку, пауза при временном отказе,
  остановка при постоянном — integration-тесты «Worker остановлен до отправки», «Ответ на отправку потерян», «Лимит
  запросов», «Нет денег»
- [x] 9.5 Восстановление: догрузка событий ордеров, сверка, прогрев, последняя свеча — integration-тесты «Рестарт
  посреди позиции», «Исполнение во время обрыва», «Свечи за долгий обрыв», «Позицию закрыли вручную», «Пропал ордер
  бота», «Неизвестный ордер бота»
- [x] 9.6 Пауза, снятие с паузы, остановка, закрытие позиции в рантайме — integration-тесты сценариев «Пауза»,
  «Снятие с паузы», «Остановка», «Закрытие позиции»
- [x] 9.7 Логи рантайма без ключей и секретов (захват, потеря аренды, отправка, отказ, расхождение) — тест
  `LogSink`, что secret не попадает в журнал

## 10. Интерфейс

- [x] 10.1 Модуль `bots`: `status`, действия и `botPositionsQuery` в `api.ts` — Vitest модуля
- [x] 10.2 Статус в `BotsPage` и опрос каждые 5 секунд — Vitest «Бот с ошибкой в списке»
- [x] 10.3 Блок статуса и действий в `BotPage`, подтверждения запуска и закрытия, отказ над действиями, недоступные
  правка и удаление — Vitest сценариев дельты `live-trading-interface`
- [x] 10.4 Позиции на странице бота на компоненте позиций деталей бэктеста — Vitest «Текущая позиция»

## 11. e2e

- [x] 11.1 `bybit-stub.ts`: WebSocket на `ws` (dev-зависимость, точная версия), `auth`, `subscribe`, `ping`, топики
  kline, `order`, `execution`; REST ордеров, истории, позиций; управляющие `/control/candle`, `/control/fill`,
  `/control/drop` — заглушка отвечает в e2e-прогоне
- [x] 11.2 `playwright.config.ts`: торгующий Worker с мастер-ключом тестов — Worker стартует в e2e
- [x] 11.3 e2e `live-trading`: запуск, свеча → ордер, исполнение → позиция, обрыв → догрузка, остановка, закрытие
  позиции — `npm run test:e2e` зелёный

## 12. Документы и проверка

- [x] 12.1 `CONTEXT.md`: «Запущенный бот», «Пауза», «Остановка», «Сверка», «Аренда бота», «Идентификатор клиента
  ордера»; «Аккаунт биржи» без окружения; «Worker» ведёт ботов — документ обновлён
- [x] 12.2 `docs/conventions.md`: область `LiveTrading`, ограждающий номер аренды, порты живой торговли — документ
  обновлён
- [x] 12.3 Пройти дифф: каждое условие нового кода требует тест — нет непокрытых веток
- [x] 12.4 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, frontend lint, format check,
  typecheck, тесты, build и `npm run test:e2e` — всё зелёное

## Workflow follow-up

- Ревью артефактов владельцем, правки через `/opsx:update`.
- `/opsx:archive` перед PR, PR `feat(trading): add live trading`.
- Деплой: `Trading__Enabled` и `ExchangeAccounts__MasterKey` прод-Worker'у — только с разрешения владельца.
