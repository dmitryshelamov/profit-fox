# Tasks

Тесты бэкенда запускаются из `backend/`: `dotnet test ProfitFox.slnx`; фронтенда — из `frontend/`: `npm test`, `npm run
typecheck`, `npm run lint`, `npm run format:check`, `npm run build`. Каждая задача начинается с теста, который без неё
падает, и каждая группа заканчивается зелёным прогоном всех тестов своей части.

Порядок групп — порядок работы. Слои и папки — по README, «Слои». Макет — `mockups/15-accounts.png`. Настоящий Bybit в
автоматических тестах не участвует. Секреты и мастер-ключ не попадают ни в репозиторий, ни в журнал, ни в вывод задач.

## 1. База

- [ ] 1.1 Снять базу до правок: `dotnet build ProfitFox.slnx --configuration Release`, тесты бэкенда и фронтенда,
  SHA-256 `/openapi/v1.json`. Проверить: числа тестов, предупреждений и хэш записаны в разделе «База» этого файла.
- [ ] 1.2 Добавить `Bybit.Net` в `Directory.Packages.props` (последняя стабильная, не ниже `7.7.0`) и ссылку в
  `ProfitFox.Infrastructure.csproj`. Проверить: сборка Release без предупреждений и без ошибок `NU1903`/`NU1904`;
  версия записана в «База».

## 2. Секрет под мастер-ключом

- [ ] 2.1 Порт `ISecretProtector` (`Application/Ports/Security`) и `AesGcmSecretProtector`
  (`Infrastructure/Security`): AES-256-GCM, nonce 12 байт, тег 16 байт, связанные данные `exchange-account:{id}:secret`
  (design, решение 1). Проверить тестами `AesGcmSecretProtectorTests`: круг «зашифровал — расшифровал», два шифрования
  одного секрета дают разный шифртекст, сценарий spec `exchange-accounts` «Подменённая строка», другой мастер-ключ не
  расшифровывает.
- [ ] 2.2 Настройка `ExchangeAccounts:MasterKey` с проверкой при старте API; ключ разработки в
  `appsettings.Development.json` и в настройках тестового приложения. Проверить тестами `MasterKeyTests`: сценарии
  «Ключа нет» и «Ключ неверной длины», в тексте ошибки нет значения ключа; Worker стартует без ключа.

## 3. Аккаунт и хранение

- [ ] 3.1 Сущность `ExchangeAccount` (`Domain/Entities/Accounts`), правила имени `AccountRules` и правила ключа
  `ExchangeKeyRules` (`Domain/Services/Accounts`; design, решение 5). Проверить тестами `AccountRulesTests` и
  `ExchangeKeyRulesTests`: сценарии «Пустой секрет», «Ключ с правом вывода», «Ключ только для чтения», «Нет права
  торговли бессрочными», «Не единый торговый аккаунт» и порядок причин, когда нарушено несколько правил.
- [ ] 3.2 Миграция `exchange_accounts` (design, решение 3) и `ExchangeAccountRepository` с портом
  `IExchangeAccountRepository`. Проверить тестами `ExchangeAccountRepositoryTests` на Testcontainers: сценарии «Секрет в
  базе», «Имя занято» (без учёта регистра), «Второй ключ того же аккаунта», «Аккаунт удалён», «Тот же UID после
  удаления»; миграция `Down` удаляет таблицу.

## 4. Клиент Bybit

- [ ] 4.1 Порт `IExchangeKeyInspector` (`Application/Ports/Exchange`) и `BybitAccountClient`
  (`Infrastructure/Clients/Bybit`): `InspectAsync` на `/v5/user/query-api`, клиент на вызов, адрес из
  `Bybit:RestAddress` (design, решение 4). Проверить тестами `BybitAccountClientTests` на `StubHttpHandler`: запрос ушёл
  на нужный путь с ключом в заголовке; ответ разобран в права, UID и вид аккаунта.
- [ ] 4.2 Разбор отказов Bybit по таблице решения 6 и исключения `ExchangeKeyRejectedException`,
  `ExchangeUnavailableException`, `ExchangeException` в `Application/Exceptions`. Проверить тестами: по одному на
  каждый `retCode` таблицы, на HTTP 403, HTTP 500, отсутствие ответа и на неизвестный код; сценарий «Секрет в журнале»
  — в записи журнала нет секрета и тела ответа `query-api`.
- [ ] 4.3 `FreeAsync` на `/v5/account/wallet-balance` (`UNIFIED`, `totalAvailableBalance`; design, решение 7).
  Проверить тестом: из ответа-образца получено `1250.40`; отклонённый ключ даёт `ExchangeKeyRejectedException`.

## 5. Команды, запросы и API

- [ ] 5.1 `AddAccountCommand` с обработчиком: порядок «поля → Bybit → правила ключа → шифрование → запись» (design,
  решение 5). Проверить тестами `AddAccountCommandHandlerTests` на подделках: сценарии «Аккаунт добавлен», «Пустой
  секрет» (к Bybit не обращались), «Bybit не знает ключ», «Bybit недоступен» (аккаунт не сохранён), «Второй ключ того же
  аккаунта» с именем уже добавленного аккаунта в причине.
- [ ] 5.2 `RenameAccountCommand`, `DeleteAccountCommand`, `GetAccountsQuery`, `GetAccountBalanceQuery`. Проверить
  тестами обработчиков: сценарии «Новое имя», «Имя другого аккаунта», «Аккаунт удалён», «Баланс показан», «Ключ больше
  не действует», «Сменили мастер-ключ».
- [ ] 5.3 `AccountsController` (`api/accounts`) с контрактами и ответами по решениям 6 и 8. Проверить тестами
  `AccountsControllerTests` на `BenchTestApplication` с `FakeKeyInspector`: коды `200`, `404`, `409`, `422` с полем,
  `502`, `503`; сценарий «Секрета в ответе нет» — в теле ответов и в `/openapi/v1.json` нет поля секрета у ответов.
- [ ] 5.4 README: раздел «Аккаунты» — что такое мастер-ключ, где он лежит в разработке и в production, какой ключ Bybit
  подходит. Проверить: команда создания мастер-ключа из README даёт значение, с которым API стартует.

## 6. Фронтенд

- [ ] 6.1 `npm run generate:api`, `src/api/accounts.ts`. Проверить: `npm run typecheck` зелёный, в `schema.d.ts` есть
  маршруты `api/accounts`.
- [ ] 6.2 Раздел «Аккаунты» в `AppShell`, маршрут `/accounts`, страница со списком и пустым состоянием. Проверить
  тестами `accounts.test.tsx` и `ui.test.tsx`: сценарии spec `bench-ui` «Текущий раздел» (семь разделов) и «Страница
  „Аккаунты“», spec `exchange-accounts` «Пустой раздел».
- [ ] 6.3 Диалог «Новый аккаунт». Проверить тестами: сценарии «Отказ в диалоге» (причина под полем, секрет очищен),
  «Идёт проверка», успешное добавление закрывает диалог и добавляет строку; поле секрета — `type="password"`.
- [ ] 6.4 Меню строки: переименование и удаление с подтверждением; баланс по кнопке. Проверить тестами: сценарии
  «Подтверждение удаления», «Баланс в строке» (число, обновление, причина отказа красным).
- [ ] 6.5 Сверить страницу с `mockups/15-accounts.png` на dev-стенде. Проверить: снимок экрана лежит в
  `mockups/after/15-accounts.png`, отличия от макета записаны в «Открыто после применения».

## 7. Проверка целиком и деплой

- [ ] 7.1 `docker-compose.prod.yml`: API получает `ExchangeAccounts__MasterKey: ${PROFIT_FOX_MASTER_KEY:?}`; проверка
  скрипта деплоя в CI учитывает новую переменную; комментарий о секретах в начале файла обновлён. Проверить:
  `docker compose -f docker-compose.prod.yml config` без переменной падает с её именем, с переменной — проходит; задание
  CI «Deploy script» зелёное.
- [ ] 7.2 Проверка на настоящем Bybit, с владельцем: на dev-стенде владелец сам вводит свой ключ. Проверить и записать
  в «Открыто после применения»: принят ли ключ, что показал баланс, что Bybit ответил на заведомо неверный секрет
  (код и HTTP-статус, без значений ключа). Без владельца задача не выполняется.
- [ ] 7.3 Весь набор: сборка Release без предупреждений, тесты бэкенда и фронтенда, `typecheck`, `lint`,
  `format:check`, `build`, `openspec validate add-exchange-accounts --strict`. Проверить: числа записаны рядом с базой
  из 1.1.

## База

Заполняется задачами 1.1 и 1.2.

## Открыто после применения

Заполняется при применении.

## Workflow follow-up

- Перед выкладкой владелец кладёт `PROFIT_FOX_MASTER_KEY` в `~/profit-fox/.env.prod` на firebat-01 (design, Migration
  Plan). Команду вводит он сам.
- Архивировать изменение после проверки владельцем; `take-idea-trades` применяется после этого изменения.
