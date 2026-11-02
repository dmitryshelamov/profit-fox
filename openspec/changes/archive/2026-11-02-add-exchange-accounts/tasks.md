# Tasks

Новая логика — строго red → green → refactor (см. `CLAUDE.md`): сначала падающий тест, потом минимальный код. Это
относится и к TypeScript: модули `model` пишутся только под падающий тест Vitest. Задача закрыта, только когда
проходят build, тесты и `dotnet format --verify-no-changes`, а для `frontend/` — ещё lint, `format:check`, typecheck,
тесты и сборка фронтенда. Сценарии ниже — из дельт `exchange-accounts`, `exchange-account-interface`,
`bot-management` и `bot-interface`. Ни один тест не обращается к живому Bybit: порты заменяются NSubstitute, e2e идут
на заглушку.

## 1. Шифрование и мастер-ключ

- [x] 1.1 `ISecretProtector` и `AesGcmSecretProtector` (TDD, `AesGcmSecretProtectorTests`): расшифровка возвращает
  исходное значение; два шифрования одного значения дают разные шифротексты; шифротекст другого аккаунта или с
  испорченным тегом не расшифровывается (design, решение 3)
- [x] 1.2 Секция `ExchangeAccounts` и `ExchangeAccountSettings`, привязка в `InfrastructureRegistrar.Configure` с
  `ValidateOnStart` (TDD, интеграционный тест хоста API): сценарии «Прод без мастер-ключа» и мастер-ключ не 32 байта —
  хост не стартует, сообщение называет `ExchangeAccounts__MasterKey` и не содержит значения; «Разработка без
  переопределения» — ключ из `appsettings.Development.json`; свой ключ в `appsettings.Testing.json`; в общем
  `appsettings.json` ключа нет. Worker секцию не получает (design, решение 4)
- [x] 1.3 `docs/conventions.md`: секция `ExchangeAccounts`, область `ExchangeAccounts` в Domain, Application,
  Infrastructure и Frontend; проверить, что описание совпадает с кодом

## 2. Домен и хранение аккаунта

- [x] 2.1 `ExchangeAccount`, `ExchangeEnvironment`, `EncryptedSecret`, `IExchangeAccountRepository`; переименование
  меняет только имя (TDD, `ExchangeAccountTests`: сценарий «Новое имя», пустое имя отклоняется)
- [x] 2.2 Миграция `CreateExchangeAccountsTableMigration` (таблица, уникальный индекс `(uid, environment)`, колонка
  `bots.exchange_account_id` с внешним ключом без каскада) и Dapper-репозиторий с `Contracts`, `Converters`, `Queries`;
  интеграционные тесты: сохранить и прочитать без secret, `GetSecretAsync`, повтор `(uid, environment)` даёт ошибку
  поля `apiKey`, тот же UID в другом окружении сохраняется, удаление аккаунта с ботом даёт `ConflictException`;
  сценарий «Secret в базе зашифрован» — в строке нет secret открытым текстом

## 3. Bybit с ключом аккаунта

- [x] 3.1 `ExchangeKeyInfo`, `ExchangeKeyRules.Check` (TDD, чистая функция): сценарии «Ключ только для чтения», «Ключ с
  правом на вывод средств», «Ключ не единого торгового аккаунта», ключ без торговли ни на споте, ни в `linear`;
  валидный ключ без ошибок
- [x] 3.2 Порт `IExchangeKeyInspector` и `BybitAccountClient.InspectAsync` (TDD, юнит-тесты на `StubHttpHandler`, как у
  `BybitMarketDataClient`): права Bybit → категории и флаги по design, решение 5; окружение →
  `Live`/`Testnet` (сценарий «Ключ проверяется в выбранном окружении»); коды отказа ключа →
  `ExchangeKeyRejectedException`; недоступность → `ExchangeUnavailableException`; клиент с ключом строится на один
  вызов
- [x] 3.3 `BybitAccountClient.GetBalanceAsync` (TDD): общий капитал и монеты с ненулевым балансом по убыванию стоимости
  в USD, всё `decimal`; отказ ключа и недоступность — как в 3.2
- [x] 3.4 При `Bybit:RestAddress` оба окружения идут на этот адрес; проверить юнит-тестом выбора окружения

## 4. Команды и запросы аккаунтов

- [x] 4.1 `ExchangeAccountCodes` и `CreateExchangeAccountCommand` (TDD, юнит-тесты обработчика): сценарии «Валидный
  аккаунт», «Пустое имя» (Bybit не вызывается), «Неизвестное окружение», пустые API key и secret, «Bybit не принимает
  ключ», ошибки `ExchangeKeyRules`, «Повторный ключ того же аккаунта», «Тот же UID в testnet», «Bybit не отвечает при
  создании» — ничего не сохраняется; «Ключ только для спота» — сохранены категории
- [x] 4.2 `GetExchangeAccountsQuery`, `GetExchangeAccountQuery` с ботами аккаунта (TDD): сценарии «Чтение сохранённого
  аккаунта», «Неизвестный аккаунт»
- [x] 4.3 `RenameExchangeAccountCommand` и `DeleteExchangeAccountCommand` (TDD): сценарии «Новое имя», «Аккаунт без
  ботов», «Аккаунт с ботом» — конфликт с именами ботов
- [x] 4.4 `GetExchangeAccountBalanceQuery` (TDD): сценарии «Баланс аккаунта», «Ключ отозван» → `ConflictException`;
  secret расшифровывается и передаётся только в порт

## 5. API аккаунтов

- [x] 5.1 `ExchangeAccountsController` и `Contracts/ExchangeAccounts` по design, решение 6, с `[ProducesResponseType]`
  на каждый ответ; интеграционные HTTP-тесты (`ProfitFoxFactory`, порт Bybit — NSubstitute): 201/400/503 создания,
  404 чтения, 409 удаления с `botNames`, 409 и 503 баланса; сценарий «Чтение аккаунта без secret» — ни один ответ
  не содержит secret
- [x] 5.2 Secret не попадает в журнал: интеграционный тест создания аккаунта с перехватом Serilog — значения secret нет
  ни в одной записи, включая ошибку Bybit

## 6. Аккаунт бота

- [x] 6.1 `Bot.ExchangeAccountId` и проверка в `StrategyLibrary.AddBot`/`UpdateBot` по категориям аккаунта (TDD):
  сценарии «Бот с аккаунтом», «Бот без аккаунта», «Ключ не торгует в категории бота», «Смена категории при аккаунте
  только для спота»
- [x] 6.2 `CreateBotCommand`/`UpdateBotCommand` читают аккаунт (TDD): сценарии «Неизвестный аккаунт», «Снять аккаунт»;
  репозиторий библиотеки пишет и читает `exchange_account_id`; интеграционный тест гонки — бот, сохранённый после
  удаления аккаунта, отклоняется, сценарий требования «Проверка аккаунта бота»
- [x] 6.3 `BotBody.ExchangeAccountId`, `exchangeAccount` в `BotResponse` и `BotSummaryResponse`, аккаунты списка ботов
  одним запросом; интеграционные HTTP-тесты: сценарий «Аккаунт в списке», существующие тесты ботов зелёные

## 7. Frontend

- [x] 7.1 Запустить API и `npm run generate:api`, закоммитить `schema.d.ts` с новыми типами
- [x] 7.2 Модуль `exchange-accounts`: `api.ts`, `model/accountProblems` (TDD, Vitest: ошибки по полям, конфликт удаления
  с именами ботов, «биржа недоступна»), `EnvironmentBadge`, `BalanceTable`, `AccountForm`
- [x] 7.3 Раздел «Аккаунты» в `AppShell` и маршруты `/accounts`, `/accounts/new`, `/accounts/$accountId`; тесты экранов
  через `renderApp` и `fakeApi`: сценарии «Переход в раздел», «Аккаунт testnet в списке», «Аккаунт добавлен», «Ключ
  отклонён» (secret очищен, имя и API key на месте), «Secret маской», «Баланс по действию» (до кнопки запроса баланса
  нет), «Биржа недоступна при чтении баланса», «Новое имя», «Подтверждённое удаление», «Аккаунт с ботами»
- [x] 7.4 Поле «Аккаунт» в `BotForm`, аккаунт и `EnvironmentBadge` на странице и в списке ботов; тесты экранов:
  сценарии «Выбор аккаунта», «Ключ не торгует в категории», «Бот на testnet», «Бот без аккаунта», пустой список
  аккаунтов предлагает добавить аккаунт

## 8. E2E и документация

- [x] 8.1 `bybit-stub.ts`: `/v5/user/query-api` и `/v5/account/wallet-balance` по design, решение 9; e2e-сценарий:
  добавить аккаунт testnet, увидеть баланс, привязать к нему бота, увидеть метку «Testnet» у бота, не суметь удалить
  аккаунт с ботом; `npm run test:e2e` зелёный
- [x] 8.2 `CONTEXT.md`: термины «Аккаунт биржи» и «Баланс аккаунта», рядом с «Балансом бота»; проверить, что «Позиция»
  и «Баланс бота» не противоречат новым терминам
- [x] 8.3 Пройти дифф: каждая ветка и проверка нового кода требуется каким-то тестом; полный прогон build, тестов,
  `dotnet format --verify-no-changes` и проверок фронтенда зелёный

## Workflow follow-up

- Владелец добавляет `ExchangeAccounts__MasterKey` в окружение API на проде до деплоя (design, Migration Plan).
- Ревью владельцем, `/opsx:archive`, затем pull request из `feat/add-exchange-accounts`.
