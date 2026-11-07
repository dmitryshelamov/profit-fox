# Tasks

Расчётная логика — строго red → green → refactor, эталонные числа посчитаны заранее (design.md, решение 3).

## 1. Расчёт

- [x] 1.1 `FeeRates.SlippagePercent` (по умолчанию `0`) и `CaseResult.R` с проскальзыванием: long по рынку до цели
  (1.8929005 R), short по рынку до цели, «Откат» без проскальзывания на входе, «Стоп-вход» до стопа (−1.105000275 R),
  истечение по close. Прежние тесты без ставки проходят без изменений.
- [x] 1.2 `IdeaMoney.PerUsdt` с проскальзыванием: long, short, «Откат», безубыток ≈ 36,84 %.
- [x] 1.3 `FeeRateSettings.Slippage` и `BacktestSettings.Policy` переносит его в `FeeRates`. Проверка: тест политики.

## 2. Настройки и потребители

- [x] 2.1 `appsettings.json` API, Scanner и Worker: `Slippage` 0,05 на `linear` и 0,1 на `spot`.
- [x] 2.2 Интеграционные тесты с числами от настроек (безубыток, прибыль на 1 USDT, расчёт входа) — новые эталонные
  числа.
- [x] 2.3 Проверить, что бэктест не читает новую ставку (тесты бэктеста проходят без изменений).

## 3. Экран

- [x] 3.1 Пояснение «Идей» и подписи «Срок жизни и цель» — «после комиссий и проскальзывания»; тесты экранов.

## 4. Документы и проверка

- [x] 4.1 `CONTEXT.md` («Случай», «Проскальзывание идей») и `docs/conventions.md` (`FeeRates`).
- [x] 4.2 Build, test, `dotnet format --verify-no-changes`; frontend lint, format check, typecheck, test, build; e2e по
  возможности.

## 5. Ревью

- [x] 5.1 Без ставки `spot` или `linear` (или со ставкой меньше нуля) API, сканер и Worker не запускаются
  (`BacktestSettings.HasIdeaSlippage`, `ValidateOnStart`). Проверка: тест настроек и тест сканера без ставки `spot`.
- [x] 5.2 Тест, что `Backtests:DefaultFees` одинаковы в `appsettings.json` трёх сервисов.
- [x] 5.3 `PaperFill` исполняет по `CaseResult.EntryFill` / `ExitFill`. Проверка: long до цели (1.89290055 USDT) и до
  стопа (−1 R), short до цели (1.89710055), «Откат» (1.9354143).
- [x] 5.4 README: «the fees and the slippage».
- [x] 5.5 Проверка свободных денег остаётся по цене входа — обоснование в design.md, решение 6.

