# Tasks

Новая логика расчёта идёт строго red → green → refactor: сначала один падающий тест, потом минимум кода. Реальных
ордеров нет: Bybit — только стаб и фейки портов.

## 1. Вид исполнения

- [x] 1.1 Проверить в Bybit.Net 7.7, что у `BybitUserTradeUpdate` (поток) есть `TradeType`, как у `BybitUserTrade`
  (REST). Результат записать в `design.md` (Open Questions → решение 2). Готово, когда ответ записан. Если поля нет,
  ветка потока в 4.2 опирается только на догрузку из 4.4.
- [x] 1.2 `ExchangeFillKind` (`Trade`, `Liquidation`, `Adl`) и `ExchangeFill.Kind` / `Side` с умолчаниями `Trade` /
  `null`. `BybitStreams` и `BybitLedger.FillsAsync` берут `BustTrade` → `Liquidation`, `AdlTrade` → `Adl`, остальное →
  `Trade`, и сторону. Готово, когда тесты `BybitLedgerTests` и потока на исполнение с `execType: "BustTrade"` /
  `"AdlTrade"` / `"Trade"` зелёные.

## 2. Домен

- [x] 2.1 `IdeaTradeLeg.Liquidation` / `Adl`, `IdeaTrade.Liquidation` / `Adl` (`LegTotals`) в `Exited`. Статусы
  `Liquidated` / `ClosedByAdl` в `Settled` и в `Result`. Готово, когда юнит-тесты `IdeaTradeTests` зелёные:
  «Ликвидация закрыла сделку» (JCTUSDT 1100 по 0.014229, результат и R), «ADL закрыла сделку», «Частичная ликвидация»
  (400 из 1000 → «Открыта» 600, затем стоп → «Закрыта по стопу», результат из обеих ног).
- [x] 2.2 `ForcedFillShares.Split(quantity, price, fee, step, holders)`. Готово, когда юнит-тесты зелёные: «Две
  сделки на символе» (300/100, 400 по 2.5, комиссия 0.4 → 0.3/0.1), исполнение больше суммы (каждый получает свой
  объём), остаток после округления до шага уходит последнему по id, нет держателей → нет долей.
- [x] 2.3 `IdeaJournalTotals`: `Liquidated` и `Adl` в счётчиках и в `settled`. Готово, когда юнит-тест «Ликвидация в
  итогах» (стоп −1.0, ликвидация −0.24, ADL +0.5 → средний −0.25, сумма и комиссии по трём) и тест сравнения
  «Ликвидированная с обещанием» зелёные.

## 3. Хранение

- [x] 3.1 Миграция `AddIdeaTradeForcedExitsMigration`: колонки `liquidation_*` и `adl_*` в `idea_trades` (`numeric NOT
  NULL DEFAULT 0`), новый `CK_idea_trades_status` с `liquidated` и `closed_by_adl`, таблица `idea_trade_forced_shares`
  (ключ `exec_id` + `trade_id`, FK с каскадом). Готово, когда тест миграции (старые строки с нулями, новые статусы
  принимаются) зелёный.
- [x] 3.2 `IdeaTradeDb`, конвертеры и запросы Dapper для новых ног. Коды `liquidated` и `closed_by_adl` в
  `IdeaTradeCodes`. Готово, когда `IdeaTradeRepositoryTests` на сохранение и чтение обеих ног и статусов зелёные.
- [x] 3.3 Методы репозитория долей (порт `IIdeaTradeRepository`): есть ли доли исполнения, вставка долей, доля сделки
  без `applied`, отметка `applied`, блокировка открытых сделок аккаунта, символа и стороны по порядку id. Готово, когда
  интеграционные тесты репозитория зелёные, включая повторную вставку того же `exec_id` без дублей.

## 4. Сессия сделки

- [x] 4.1 `IExchangeLedger.GetForcedFillsAsync(key, instrument, since)`: в `BybitLedger` два запроса
  `GetUserTradesAsync` с `execType` и курсором, начало — `max(since, сейчас − 7 дней)`. В `TInvestLedger` —
  `ExchangeException`, в `RoutedExchangeLedger` — передача дальше. Готово, когда тесты `BybitLedgerTests` (параметры
  запроса, страницы), `TInvestLedgerTests` и `RoutedTradingPortsTests` зелёные.
- [x] 4.2 `ForcedFillInput` в `IdeaTradeSession`: исполнение `Kind != Trade` по инструменту сделки разбирается раньше
  `Mine` и не планирует сверку. Применение по решению 3 design (блокировки по id, доли один раз, своя доля с
  `applied`). Готово, когда юнит-тесты сессии со стабом зелёные: «Ликвидация закрыла сделку», «Две сделки на символе»
  (каждая сессия применяет свою долю), «Исполнение пришло дважды», «Сделка другой стороны».
- [x] 4.3 После `Liquidated` / `ClosedByAdl` сессия снимает стоящие стоп и цель и сохраняет исполнения своих ордеров.
  Готово, когда юнит-тест сессии на снятие оставшихся дочерних ордеров после ликвидации зелёный.
- [x] 4.4 Догрузка в `CatchUpAsync` открытой сделки на `linear` с `OpenedAt`. Готово, когда тесты зелёные:
  «Ликвидация, пока Worker лежал» (после `RebuildAsync` сделка «Ликвидирована») и «Ликвидация не уронила сделку»
  (исполнение без потока находится перед повтором сверки, сделка не «Ошибка»).
- [x] 4.5 Интеграционный тест Worker'а на Bybit-стабе (`Tests.Integration/Workers/IdeaTradingTests.cs`):
  - открытая сделка long получает из потока исполнение `BustTrade` на весь объём;
  - позиция стаба становится `0`;
  - сделка «Ликвидирована» с результатом;
  - сверка ничего не роняет.

  Готово, когда тест зелёный.

## 5. API и экран

- [x] 5.1 Поля `liquidated` и `adl` в ответе итогов журнала, коды статусов в описании ответа. Сгенерированная схема
  фронтенда обновлена. Готово, когда `IdeaTradesApiTests` на итоги с ликвидированной сделкой и сгенерированная схема
  (`npm run typecheck`) зелёные.
- [x] 5.2 Подписи «Ликвидирована» и «Закрыта биржей (ADL)» в `status.ts`. В `outcomeCountsText` — «ликвидировано» и
  «ADL», только когда они больше нуля; в `closedCountText` — новые счётчики. Готово, когда тесты `status.test.ts`,
  `journal.test.ts` («Есть ликвидация») и страницы журнала («Ликвидированная сделка» в таблице) зелёные.

## 6. Проверка

- [x] 6.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, а во `frontend/` — `npm run lint`,
  `format:check`, `typecheck`, `test`, `build` и `test:e2e`. Готово, когда всё зелёное.

## Workflow follow-up

- Archive, PR, squash-merge, деплой через Approve.
- После деплоя, с отдельного «да» владельца, правка строки JCTUSDT `aaec349d-8857-4a61-bc98-853d18ebafbb` по
  исполнению `BustTrade` на Bybit (Migration Plan в `design.md`).
