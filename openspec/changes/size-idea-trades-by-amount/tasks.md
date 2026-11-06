# Tasks

Новая логика идёт строго по TDD: красный тест, минимальный код, рефакторинг. Тесты и локальные запуски ходят только в
заглушку Bybit или в подменённый транспорт.

## 1. Домен

- [ ] 1.1 `IdeaTradeSizing.Calculate(plan, fees, amount, rules)`: объём по сумме вниз до шага, настоящая сумма, убыток
  по стопу, прибыль по цели, R:R. `IdeaTradeSize` без `CappedByAmount`, с `Profit`. Готово, когда NUnit на сценариях
  «Объём по сумме» (0.47, 47.00, ≈0.99, ≈1.83, 1.84) и «Сумма меньше шага» (объём 0) зелёный, а тесты на урезание по
  пределу удалены.
- [ ] 1.2 Причина `AmountAboveMax` в `IdeaTradeRefusals`: `IdeaTradingLimits` получает `MaxAmount`, проверяется
  введённая сумма. Готово, когда NUnit на «Сумма упёрлась в предел» называет причину, а сумма, равная пределу, — нет.
- [ ] 1.3 `IdeaTradingSettings.Risk` → `Amount`, `Default` с суммой 20. Готово, когда домен собирается и тесты домена
  зелёные.

## 2. Хранение

- [ ] 2.1 Миграция `202611070008` `ReplaceIdeaTradingRiskWithAmountMigration`: `amount numeric NOT NULL DEFAULT 20`,
  default снят, `risk` удалён. Репозиторий и конвертер настроек на `amount`. Готово, когда интеграционный тест
  репозитория читает настройки по умолчанию с суммой 20, а сохранённую сумму — как сохранили.

## 3. Application и API

- [ ] 3.1 `IdeaTradeEntryCalculator.PrepareAsync(ideaId, accountId, amount)`: без суммы берёт её из настроек, сумма не
  больше нуля даёт 400 по `amount`. `GetIdeaEntryQuery` и `CreateIdeaTradeCommand` берут `amount`. Готово, когда
  интеграционные тесты `IdeaEntryApiTests` на объём по сумме, 400 по `amount` и `amount_above_max` зелёные.
- [ ] 3.2 `IdeaEntryResponse` с `profit`, без `cappedByAmount`. `IdeaTradeBody.Amount` обязателен,
  `IdeasController.Entry` берёт `?amount`. Тело и ответ настроек с `amount`, проверка `amount > 0`. Коды причин с
  `amount_above_max`. Готово, когда `IdeaTradesApiTests` и тесты API настроек зелёные, а `POST` без `amount` даёт 400.
- [ ] 3.3 Сгенерировать заново `frontend/src/shared/api/schema.d.ts`. Готово, когда в схеме есть `amount` и `profit`,
  а `risk` в теле входа и `cappedByAmount` нет.

## 4. Фронтенд

- [ ] 4.1 `EntryDialog`: поле «Сумма, USDT»; строки «Сумма», «Убыток по стопу», «Прибыль по цели» через
  `showDecimal(…, 2)`; без пояснения про предел; запрос и вход с `amount`. Готово, когда тесты
  `marketPairEntry.test.tsx` на сценарии «Убыток и прибыль по сумме», «Сумма упёрлась в предел» и вход с `amount` зелёные.
- [ ] 4.2 `SettingsPanel` и `settingsForm`: «Сумма на сделку, USDT» вместо «Риск на сделку», поле `amount`. Тексты
  причин с `amount_above_max`. Фикстуры `tradingSettings` и `entryOf` на новые поля. Готово, когда тесты настроек и
  `reasons.test.ts` зелёные.
- [ ] 4.3 E2E `idea-trades.spec.ts`: настройки с `amount`, вход по сумме на заглушке. Готово, когда `npm run test:e2e`
  по этому файлу зелёный.

## 5. Проверка

- [ ] 5.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; во фронтенде lint, format check,
  typecheck, тесты, build. Готово, когда всё зелёное.
- [ ] 5.2 Пройти по диффу: каждая ветка и проверка нового кода нужна какому-то тесту. Готово, когда лишнего нет.

## Workflow follow-up

- `/opsx:archive`, затем PR `feat(idea-trades): size idea trades by amount`.
- После выкатки напомнить владельцу задать свою «Сумму на сделку»: миграция ставит 20.
