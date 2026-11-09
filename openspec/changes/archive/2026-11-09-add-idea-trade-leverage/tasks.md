# Tasks

Новая логика расчёта идёт строго red → green → refactor: сначала один падающий тест, потом минимум кода.

## 1. Домен

- [x] 1.1 `IdeaTradingSettings.Leverage` (`int`, по умолчанию `1`) и `IdeaTrade.Leverage`; `IdeaTrade.Create` берёт
  плечо, на `spot` всегда `1`. Готово, когда юнит-тесты на настройки по умолчанию и на плечо сделки `linear`/`spot`
  зелёные.
- [x] 1.2 `IdeaTradeRefusal.StopTooFarForLeverage`: правило `|entry − stop| / entry ≥ 1 / (2L)` только на `linear` в
  `IdeaTradeRefusals.Check`, `DetailOf` с долей стопа и пределом в процентах. Готово, когда юнит-тесты на сценарии «Стоп
  дальше ликвидации» (100/88 при 5× → 12 % и 10 %), «Стоп при плече 1×» (short 100/140) и на `spot` без причины зелёные.
- [x] 1.3 `IdeaTradeRefusal.LeverageDiffers`: `IdeaTradeAccount` несёт плечо символа и занятость символа; причина только
  на `linear`, когда символ занят и плечо другое; `DetailOf` с плечом символа и настройкой. Готово, когда юнит-тесты на
  «Чужая позиция с другим плечом», «Открытый ордер с другим плечом» и «Пустой символ с другим плечом» зелёные.
- [x] 1.4 Залог в проверке денег: в `StartFunds.Enough` идёт `size.Amount / leverage`, ждущие сделки дают
  `Amount / Leverage`. Готово, когда юнит-тесты на «Денег хватает на залог» (100 при 5×, 30 свободных) и «Ждущая сделка
  занимает свой залог» (20 + 25 > 40) зелёные.
- [x] 1.5 `PaperEntry.Check` получает плечо настроек и отказывает с `StopTooFarForLeverage` на `linear`. Готово, когда
  юнит-тесты на «Учебный со стопом дальше ликвидации» и «Учебный на споте при плече» зелёные.
- [x] 1.6 `IdeaTradeRefusal.LeverageNotSet` без чисел. Готово, когда тест текстов причин в `DetailOf` (без чисел) зелёный.

## 2. Порты и Bybit

- [x] 2.1 `IExchangeLedger.GetLeverageAsync` и `BybitLedger`: поле `leverage` строки символа из `/v5/position/list`.
  Готово, когда тест `BybitLedgerTests` на ответ с `leverage: "10"` при нулевой позиции зелёный.
- [x] 2.2 `IOrderGateway.SetLeverageAsync` и `BybitOrderGateway`: `/v5/position/set-leverage` с равными `buyLeverage` и
  `sellLeverage`; код `0` и `110043` → `Accepted`, отказ → `Rejected` с сообщением, сбой из `BybitCodes` или молчание →
  `Unavailable`. Готово, когда тесты `BybitOrderGatewayTests` на тело запроса и на четыре исхода зелёные.
- [x] 2.3 Обновить фейки портов в тестах (`Tests.Unit`, `Tests.Integration`), чтобы они знали плечо символа и запоминали
  установку. Готово, когда `dotnet build` решения зелёный.

## 3. Хранение

- [x] 3.1 Миграция FluentMigrator: `leverage int NOT NULL DEFAULT 1` в настройках торговли идеями и в сделках по идеям;
  конвертеры и запросы Dapper. Готово, когда тест миграции (старые строки получают `1`) и `IdeaTradingSettingsRepositoryTests`,
  `IdeaTradeRepositoryTests` с плечом зелёные.

## 4. Приложение

- [x] 4.1 `IdeaTradeEntryCalculator.EvaluateAsync`: на `linear` с ключом читает плечо, позицию и открытые ордера символа,
  передаёт их в `IdeaTradeAccount`; `EntryCalculation` несёт `Leverage` и `Margin`. Готово, когда юнит-тесты калькулятора
  на «Залог при плече», «Плечо на споте» и на причину `LeverageDiffers` с фейком зелёные.
- [x] 4.2 `CreateIdeaTradeCommandHandler`: под блокировкой после проверок, если плечо символа не равно настройке, вызывает
  `SetLeverageAsync`; `Rejected` → `IdeaTradeRefusedException([LeverageNotSet])` без сделки и с сообщением Bybit в логе;
  `Unavailable` → `ExchangeUnavailableException`; сделка запоминает плечо. Готово, когда тесты на «Символ с плечом 10×»,
  «Плечо уже нужное» (Bybit не вызван), «Bybit отказал» и «Вход на споте» зелёные.
- [x] 4.3 `RunNextAutoEntryCommandHandler.EnterAsync`: то же для автомата с деньгами; `Rejected` пишет отказ
  `LeverageNotSet`, `Unavailable` оставляет запрос на следующий проход, как молчание биржи в `PrepareAsync`. Готово, когда
  тесты `RunNextAutoEntryTests` на установку плеча, отказ и молчание зелёные.
- [x] 4.4 Автомат «Без денег» передаёт плечо настроек в `PaperEntry.Check`. Готово, когда тест учебного входа на `linear`
  со стопом 12 % при 5× пишет отказ.
- [x] 4.5 `SaveIdeaTradingSettings`: плечо меньше 1 → 400 по `leverage`. Готово, когда тест валидации зелёный.

## 5. API

- [x] 5.1 Тело и ответ настроек с `leverage`; ответ `GET /api/ideas/{id}/entry` с `leverage` и `margin`; коды причин
  `stopTooFarForLeverage`, `leverageDiffers`, `leverageNotSet`. Готово, когда интеграционные тесты API настроек (по
  умолчанию `1`, `0` и `2.5` → 400 по полю), `IdeaEntryApiTests` (плечо и залог) и `IdeaTradesApiTests` (409 с
  `leverageNotSet`) зелёные.
- [x] 5.2 Интеграционный тест Worker'а (`IdeaTradingTests`): вход на `linear` после установки плеча ставит ордер как
  раньше. Готово, когда тест зелёный.
- [x] 5.3 Сгенерировать заново `frontend/src/shared/api/schema.d.ts`. Готово, когда в схеме есть `leverage`, `margin` и
  новые коды.

## 6. Фронтенд

- [x] 6.1 `SettingsPanel` и `settingsForm`: поле «Плечо на фьючерсах» с подсказкой из спеки, ошибка валидации у поля.
  Готово, когда тесты настроек на подпись, сохранение `3` и ошибку при `0` зелёные.
- [x] 6.2 `EntryDialog`: на `linear` строки «Плечо 5×» и «Залог 9.40 USDT», на `spot` их нет. Готово, когда тесты окна
  входа на «Плечо и залог на фьючерсах» и «Без плеча на споте» зелёные.
- [x] 6.3 Тексты трёх новых причин в `reasons` и в отказах автоматов. Готово, когда `reasons.test.ts` с «Стоп дальше, чем
  позволяет плечо: 12 % при пределе 10 %», «На символе открыта позиция с плечом 1× — плечо не поменять» и «Bybit не дал
  поставить плечо» зелёный.
- [x] 6.4 `bybit-stub.ts`: плечо по символу (по умолчанию `10`), `/v5/position/set-leverage` с `110043` на то же плечо,
  `positionList` отдаёт сохранённое; `bybit-stub.spec.ts` на это. E2E `idea-trades.spec.ts`: после входа на `linear`
  плечо символа в заглушке `1`. Готово, когда `npm run test:e2e` по этим файлам зелёный.

## 7. Проверка

- [x] 7.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; во фронтенде lint, format check,
  typecheck, тесты, build. Готово, когда всё зелёное.
- [x] 7.2 Пройти по диффу: каждая ветка и проверка нового кода нужна какому-то тесту. Готово, когда лишнего нет.

## 8. Слияние с main и исправления после ревью

- [x] 8.1 Слить `origin/main` в ветку (merge, без rebase), сохранив поведение обеих сторон; `SetLeverageAsync` в
  `RoutedOrderGateway` (по бирже ключа) и в `TInvestOrderGateway` (`Permanent` без вызова), плечо в `RoutedExchangeLedger`
  и `TInvestLedger`; номер миграции `202611090001` уникален и позже всех миграций `main`. Готово, когда тесты
  маршрутизации и T-Invest зелёные.
- [x] 8.2 `IdeaTrade.Create`: плечо только на `linear`, на `moex` и `spot` — `1`. Готово, когда тест на `moex` зелёный.
- [x] 8.3 Плечо старых сделок неизвестно: `IdeaTrade.Leverage` — `int?`, `idea_trades.leverage` — `NULL` у старых строк,
  залог сделки с неизвестным плечом — вся сумма; API отдаёт `leverage` и `margin` сделки (`null` у неизвестного), «Сделки в
  работе» показывают «5× · 21.00 USDT» или прочерк; бумажные сделки читаются с `1`. Готово, когда тесты домена, миграции,
  API журнала и фронтенда зелёные.
- [x] 8.4 Гонка плеча: символ занят и сделкой аккаунта «Ждёт входа» или «Открыта» на нём; `LeverageDiffers`, если настройка
  не равна плечу биржи или известному плечу такой сделки; `LeverageToSet` пуст, пока символ занят. Готово, когда тесты
  `IdeaTradeRefusals`, калькулятора и сценарий «Ждущая сделка не даёт поменять плечо» в `CreateIdeaTradeTests` зелёные.
- [x] 8.5 Один `GetLinearPositionAsync` (плечо и позиция из одного `/v5/position/list`) вместо `GetLeverageAsync` и
  `GetHoldingAsync`, параллельно с открытыми ордерами. Готово, когда тесты `BybitLedgerTests` (один запрос) и калькулятора
  (одно чтение, ордера читаются, пока читается позиция) зелёные.
- [x] 8.6 Пустой список позиций или строка без `leverage` — `ExchangeException`. Готово, когда тест `BybitLedgerTests`
  на оба случая зелёный.
- [x] 8.7 Плечо `1e12` или `-1e12` в настройках — 400 по полю `leverage`, а не 500. Готово, когда тест API настроек
  зелёный.
- [x] 8.8 `IdeaEntryResponse.Of` берёт первую деталь каждого кода причины. Готово, когда тест ответа с повторённой
  причиной зелёный.
- [x] 8.9 Текст `leverage_differs`: «Символ занят позицией, ордером или сделкой с плечом N× — плечо не поменять», у
  автоматов «Символ занят с другим плечом». Готово, когда `reasons.test.ts` и `labels.test.ts` зелёные.
- [x] 8.10 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; во фронтенде lint, format check,
  typecheck, тесты, build. Готово, когда всё зелёное.

## Workflow follow-up

- `/opsx:archive`, затем PR `feat(idea-trades): set the leverage of idea trades on futures`.
- После выкатки первый реальный вход на `linear` — под присмотром владельца: проверить на Bybit плечо символа 1×.
