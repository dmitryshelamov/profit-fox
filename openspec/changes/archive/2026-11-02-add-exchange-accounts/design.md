# Design

## Context

Мотивация — в `proposal.md`, требования — в `specs/`. Текущее устройство:

- Входа и пользователей нет: API (`ProfitFox.Presentation`) открыт локальной сети, web-образ Caddy проксирует его.
- Bybit доступен только публично: `BybitMarketDataClient` ходит через `IBybitRestClient` из Bybit.Net 7.7 без ключа.
  `services.AddBybit` настраивается в `InfrastructureRegistrar`; при `Bybit:RestAddress` всё REST идёт на заглушку
  (`BybitEnvironment.CreateCustom`).
- Бот (`Entities/Bots/Bot`) — часть агрегата `StrategyLibrary`, сохраняется вместе с библиотекой в одной транзакции с
  проверкой ревизии; `bots` ссылается на `strategies` внешним ключом без каскада.
- Ошибки: `ValidationFailedException` → 400 с ошибками по полям, `ExchangeUnavailableException` → 503,
  `ConflictException` → 409 (`StrategyProblems.Of` в контроллерах).
- Bybit.Net 7.7 даёт `BybitRestClient` с `BybitCredentials` и окружением в опциях,
  `V5Api.Account.GetApiKeyInfoAsync` (`BybitApiKeyInfo`: `Readonly`, `Uta`, `UserId`, `Permissions`) и
  `V5Api.Account.GetBalancesAsync(AccountType.Unified)`.

## Goals / Non-Goals

**Goals:**

- Secret расшифровывается только в момент запроса к Bybit и нигде не оседает: ни в ответах, ни в журнале, ни в кэше
  клиентов SDK дольше запроса.
- Прод с забытым мастер-ключом падает при деплое, а не шифрует ключи публичным ключом из git.
- Изменение 5 получает готовые порт «клиент Bybit с ключом аккаунта» и связь бота с аккаунтом.

**Non-Goals:**

- Ротация мастер-ключа и перешифрование; замена ключа у аккаунта; региональные домены Bybit (EU, Turkey и т. п.) и Demo
  trading; периодическая перепроверка ключей.
- Worker не расшифровывает ключи и не получает мастер-ключ (см. решение 4).

## Decisions

### 1. Решения из обсуждения с владельцем

Согласованы 2026-11-02 (`/grill-me`):

- Ключи вводятся в UI и хранятся в базе в зашифрованном виде (AES-GCM, мастер-ключ), как `ExchangeCredential` и
  `AesGcmSecretProtector` в ProfitGraph.
- Мастер-ключ по умолчанию лежит только в `appsettings.Development.json`, как учётные данные dev-базы; для тестов свой в
  `appsettings.Testing.json`. В общем `appsettings.json` ключа нет; вне этих окружений сервис без мастер-ключа не
  стартует. На проде ключ задаётся переменной окружения.
- Входа и пользователей нет. Владелец отклонил пароль владельца, когда ему объяснили риск: любое устройство сети или
  межсайтовый запрос из браузера может вызвать API, который хранит торговые ключи. Вернуться к этому — вопрос изменения 5.
- Аккаунт = имя, окружение (основная сеть или testnet), API key и secret; API key показывается целиком, secret — только
  маской; смена ключа = удалить и создать. Проверка у Bybit при сохранении: неверный ключ, ключ без права на торговлю и
  ключ с правом на вывод отклоняются; UID сохраняется, повтор UID в том же окружении запрещён. Баланс по запросу, не
  хранится. У бота необязательный аккаунт; аккаунт с ботами не удаляется; боты не запускаются.
- Принятые значения по умолчанию: раздел «Аккаунты» в шапке, баланс по кнопке, поле «Аккаунт» в форме бота и метка
  testnet у бота; Bybit недоступен → 503, Bybit отклонил ключ → ошибка поля; подписанные запросы проверяются юнит-тестами,
  в e2e — заглушка; термины в `CONTEXT.md`; макетов в Penpot нет.

### 2. Аккаунт — отдельный агрегат, бот ссылается на него по идентификатору

`Entities/ExchangeAccounts/ExchangeAccount` (id, имя, `ExchangeEnvironment` `Mainnet`/`Testnet`, API key,
`EncryptedSecret`, UID, набор `MarketCategory`, `CreatedAt`) с `IExchangeAccountRepository`. У `Bot` появляется
`Guid? ExchangeAccountId`.

Проверку «ключ торгует в категории бота» делает обработчик команды бота: он читает аккаунт и передаёт его категории в
`StrategyLibrary.AddBot`/`UpdateBot` (доменное правило остаётся в библиотеке, как остальные проверки бота). Целостность
при гонке с удалением аккаунта держит внешний ключ `bots.exchange_account_id → exchange_accounts(id)` без каскада:
удаление аккаунта, к которому успели привязать бота, падает нарушением ключа, и репозиторий превращает его в
`ConflictException`. Удаление сначала ищет ботов аккаунта, чтобы ответить конфликтом с их именами.

Отклонено: аккаунт внутри `StrategyLibrary` — библиотека и так большая, а ключи к стратегиям отношения не имеют, и
каждое сохранение стратегии загружало бы зашифрованные ключи.

Категории торговли — снимок прав ключа на момент создания. Если владелец урежет права ключа на Bybit, система этого не
увидит до изменения 5, где ордер всё равно вернёт ошибку биржи.

### 3. Шифрование: AES-256-GCM, только secret

Порт `Ports/Security/ISecretProtector` (`Encrypt(accountId, plaintext)` → `EncryptedSecret`, `Decrypt`), реализация
`Infrastructure/Security/AesGcmSecretProtector`: свежий 12-байтовый nonce на значение, 16-байтовый тег, связанные данные
`exchange-account:{id:D}:secret` привязывают шифротекст к строке. В базе — `secret_nonce`, `secret_ciphertext`,
`secret_tag` (`bytea`). API key не шифруется: он показывается целиком и без secret бесполезен.

Мастер-ключ — секция `ExchangeAccounts`, значение `MasterKey` (base64 от 32 байт), запись `ExchangeAccountSettings` в
Application. Проверка — `ValidateOnStart` на options: пустое или не 32 байта → хост не стартует с сообщением, где
сказано имя настройки (`ExchangeAccounts__MasterKey`), но не значение. Отдельной ветки «только для Production» нет:
ключ по умолчанию есть лишь в файлах Development и Testing, поэтому любое другое окружение без переменной падает само.

Отклонено: ASP.NET Data Protection — хранит ключи в файловой системе контейнера и шифрует своим кольцом ключей; потеря
тома незаметно делает все secret нечитаемыми. Ротация мастер-ключа (идентификатор ключа в строке, как в ProfitGraph) не
нужна ни одному тесту сейчас; колонку легко добавить миграцией, когда ротация понадобится.

### 4. Worker не получает мастер-ключ

В этом изменении ключи расшифровывает только API (проверка ключа и баланс). Требовать мастер-ключ у Worker'а значило бы
разнести его на все машины с Worker'ами, которые считают только бэктесты, и уронить их при обновлении, пока в
`worker.env` нет переменной. Секцию и проверку получит тот Worker, который будет торговать, в изменении 5. Это отступление
от значения по умолчанию, названного в обсуждении («мастер-ключ получает и Worker»); владелец подтвердил его
2026-11-02 вместе с решениями о ключах только UTA (решение 5) и категориях торговли (решение 2).

### 5. Bybit с ключом аккаунта

Порт `Ports/ExchangeAccounts/IExchangeKeyInspector`:

- `InspectAsync(environment, apiKey, secret)` → `ExchangeKeyInfo(Uid, IsReadOnly, IsUnifiedAccount, CanWithdraw,
  TradableCategories)`;
- `GetBalanceAsync(environment, apiKey, secret)` → `AccountBalance(TotalEquityUsd, Coins: [Coin, WalletBalance, UsdValue])`,
  монеты с ненулевым балансом в порядке убывания стоимости в USD.

Реализация `Exchanges/Bybit/BybitAccountClient` (типизированный `HttpClient`) строит `BybitRestClient` с ключом на каждый
вызов и освобождает его после вызова, поэтому secret не остаётся ни в каком кэше SDK. От `IBybitUserClientProvider`
отказались при реализации: он кэширует клиентов по идентификатору и сам строит `HttpClient`, а тесты адаптера подменяют
HTTP через `StubHttpHandler`, как у `BybitMarketDataClient`. Окружение → `BybitEnvironment.Live` или
`BybitEnvironment.Testnet`; при заданном `Bybit:RestAddress` оба окружения идут на этот адрес (e2e). Права: `Spot` содержит `SpotTrade` → `spot`; `ContractTrade` содержит `Order` и `Position` → `linear`;
`Wallet` содержит `Withdraw` → `CanWithdraw`; `Uta` → `IsUnifiedAccount`. Одна попытка без повторов, как проверка
инструмента: пользователь ждёт ответ синхронно.

Ошибки: коды Bybit «ключ неверен, просрочен, подпись не сошлась, IP не разрешён» (`10003`, `10004`, `10005`, `10010`,
`33004`) → `ExchangeKeyRejectedException` (в Application, без имени биржи); недоступность → `ExchangeUnavailableException`.
Команда создания превращает отказ в ошибку поля `apiKey`; запрос баланса — в `ConflictException` (409), потому что
ключ уже сохранён и поле исправлять негде. Прочий отказ Bybit (например, `10002` при уплывших часах сервера) — 502 с
причиной Bybit, а не 503: повтор не поможет. Secret, который текущий мастер-ключ не расшифровывает, при запросе баланса —
409 с причиной «пересоздайте аккаунт». Добавлено после ревью. Ответ Bybit целиком не логируется: `BybitApiKeyInfo` содержит поле `Secret`.

Проверка прав — чистая функция `ExchangeKeyRules.Check(ExchangeKeyInfo)` → список ошибок по `apiKey` в Application;
порядок проверок: только чтение, не UTA, вывод средств, нет торговли.

### 6. API

- `GET /api/exchange-accounts` → `[{ id, name, environment, apiKey, uid, categories, createdAt }]`.
- `GET /api/exchange-accounts/{id}` → то же плюс `bots: [{ id, name }]`; 404.
- `POST /api/exchange-accounts` `{ name, environment, apiKey, apiSecret }` → 201 `{ id }`; 400 по полям; 503.
- `PUT /api/exchange-accounts/{id}` `{ name }` → 204; 400; 404.
- `DELETE /api/exchange-accounts/{id}` → 204; 404; 409 с `botNames` в `ProblemDetails`.
- `GET /api/exchange-accounts/{id}/balance` → `{ totalEquityUsd, coins: [{ coin, walletBalance, usdValue }] }`
  десятичными строками; 404; 409 (ключ отклонён); 503.

Коды окружения `mainnet`/`testnet` разбираются в `ExchangeAccountCodes` (Application). Бот: в `BotBody` поле
`exchangeAccountId` (необязательное), в `BotResponse` и `BotSummaryResponse` — `exchangeAccount: { id, name,
environment } | null`. Список ботов собирает аккаунты одним запросом к репозиторию аккаунтов, без secret.

Repository читает secret только в `GetSecretAsync(id)`; список и чтение аккаунта secret не выбирают, чтобы
расшифрованное значение не появлялось там, где оно не нужно.

### 7. Хранение

Миграция `CreateExchangeAccountsTableMigration`: `exchange_accounts (id, name, environment, api_key, secret_nonce,
secret_ciphertext, secret_tag, uid, categories text[], created_at)`, уникальный индекс `(uid, environment)` — повтор,
проскочивший гонку, даёт нарушение индекса, и репозиторий отвечает той же ошибкой поля, что проверка до записи. Колонка
`bots.exchange_account_id uuid NULL` с внешним ключом без каскада. CHECK-ограничения пускают только известные коды
окружения и категорий, а конвертер строки падает на неизвестном коде: неизвестное окружение никогда не читается как
основная сеть (добавлено после ревью).

### 8. Frontend

Модуль `modules/exchange-accounts`: `api.ts` (запросы, ключи кэша), `ui/AccountForm`, `ui/EnvironmentBadge`,
`ui/BalanceTable`, `model/accountProblems` (ошибки сервера по полям, конфликт удаления с именами ботов). Страницы
`accounts` (список, `/accounts`), `account-new` (`/accounts/new`), `account` (`/accounts/$accountId`: сведения,
переименование на месте, удаление, «Показать баланс» — запрос только по кнопке, `enabled: false` + `refetch`). Поле secret —
`type="password"`, `autocomplete="off"`; после ошибки сервера secret очищается. `BotForm` получает `<select>` аккаунта из
списка аккаунтов; страница и список ботов показывают аккаунт и `EnvironmentBadge`.

### 9. Заглушка Bybit

`frontend/e2e/bybit-stub.ts` отвечает на `/v5/user/query-api` и `/v5/account/wallet-balance`, не проверяя подпись.
Ответ выбирается по заголовку `X-BAPI-API-KEY`: ключ `e2e-valid-*` — UTA с правами на спот и `linear`, без вывода, UID —
число из ключа; `e2e-withdraw` — с правом `Withdraw`; `e2e-rejected` — `retCode 10003`. Баланс детерминированный:
`1000` USDT и `0.01` BTC.

## Risks / Trade-offs

- [API открыт сети, а хранит торговые ключи] → ключи без права на вывод, secret не выдаётся; вход — вопрос изменения 5.
  Риск принят владельцем.
- [Потеря мастер-ключа делает все secret нечитаемыми] → аккаунты удаляются и создаются заново; ключи на Bybit владелец
  может перевыпустить. Других секретов мастер-ключ не защищает.
- [Ключ протух или права урезали после создания] → баланс отвечает 409 с причиной; категории — снимок на момент
  создания (решение 2).
- [Клиент Bybit.Net держит secret] → клиент строится на один вызов и освобождается после него.
- [Testnet и основная сеть различаются списком инструментов] → инструмент бота проверяется по основной сети, как сейчас;
  в изменении 5 это может потребовать проверки по окружению аккаунта.

## Migration Plan

1. До деплоя владелец добавляет `ExchangeAccounts__MasterKey` (base64 от 32 случайных байт, например
   `openssl rand -base64 32`) в окружение API на проде; без него новый API не стартует. Файлы прода агент не трогает.
2. Миграция добавляет таблицу и nullable-колонку; существующие боты остаются без аккаунта.
3. Откат: старый API игнорирует новую таблицу и колонку; миграция только вперёд, как остальные.

## Open Questions

- Нужен ли вход перед запуском ботов на реальные деньги (изменение 5).
- Проверять ли инструмент бота по окружению его аккаунта (изменение 5).
