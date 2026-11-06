# Design

## Context

Как сделано сейчас:
- `IdeaTradeSizing.Calculate(plan, fees, risk, maxAmount, rules)` считает объём по риску (`risk / (LossPerUsdt × entry)`)
  и урезает его по «Не больше суммы на сделку». Флаг `CappedByAmount` говорит, что объём урезан.
- `IdeaTradeSize(Quantity, Amount, Risk, RiskReward, CappedByAmount)`. Здесь `Risk` — убыток по стопу после комиссий
  (`Amount × LossPerUsdt`), его же хранит `IdeaTrade.Risk`. От него считаются R и «Не больше общего риска».
- `IdeaTradeEntryCalculator.PrepareAsync(ideaId, accountId, risk)` берёт риск из настроек, если его не передали, и
  отвечает 400 на риск не больше нуля. `CreateIdeaTradeCommand(IdeaId, AccountId, Risk)` делает тот же расчёт под
  блокировкой.
- Настройки: `IdeaTradingSettings(AccountId, Risk, MaxAmount, MaxOpenTrades, MaxTotalRisk, MinRiskReward)`, таблица
  `idea_trading_settings` с колонкой `risk`. На проде строка уже сохранена.
- `IdeaMoney.PerUsdt(plan, fees)` отдаёт `ProfitPerUsdt` и `LossPerUsdt` с комиссиями входа и выхода.
- Фронтенд: `EntryDialog` с полем «Риск, USDT», `SettingsPanel` и `settingsForm` с полем `risk`, e2e
  `idea-trades.spec.ts` сохраняет настройки с `risk: 1`.

## Goals / Non-Goals

**Goals:**
- Размер сделки задаётся суммой. Убыток и прибыль видны в окне до входа.
- Хранение сделки, R, общий риск, Worker и ордера на бирже не меняются.

**Non-Goals:**
- Переключатель «сумма или риск».
- Плечо: сумма — это деньги при 1×, как и раньше.
- Изменения журнала и экрана «Сделки»: там уже показываются риск и результат в R.

## Decisions

### 1. Объём по сумме, без урезания

`IdeaTradeSizing.Calculate(plan, fees, amount, rules)`:
- `quantity = floor(amount / entry / step) × step`;
- настоящая сумма = `quantity × entry`;
- `Risk` = настоящая сумма × `LossPerUsdt`;
- `Profit` = настоящая сумма × `ProfitPerUsdt`;
- `RiskReward` = `ProfitPerUsdt / LossPerUsdt`.

`IdeaTradeSize` становится `(Quantity, Amount, Risk, Profit, RiskReward)`: `CappedByAmount` уходит, `Profit`
добавляется. `MaxAmount` в расчёт объёма больше не передаётся.

Отвергнуто: урезать сумму по пределу, как раньше. Владелец сам вводит сумму, и молча поставить другую — значит
войти не на ту сумму, которую он видел.

### 2. Предел суммы — причина отказа

Новая причина `IdeaTradeRefusal.AmountAboveMax`, код `amount_above_max`, текст «Сумма больше предела на сделку». Она
проверяется по сумме, которую ввёл владелец, а не по настоящей после округления: настоящая не бывает больше
введённой. `IdeaTradingLimits` получает `MaxAmount`, проверка стоит рядом с R:R в фиксированном порядке причин.

### 3. Сумма в API

- `GET /api/ideas/{id}/entry?accountId&amount`: `amount` вместо `risk`. Без `amount` берётся сумма из настроек.
  Сумма не больше нуля даёт 400 по полю `amount`.
- `POST /api/idea-trades` с телом `{ ideaId, accountId, amount }`.
- `IdeaEntryResponse`: `risk` остаётся убытком по стопу, добавляется `profit`, `cappedByAmount` уходит.
- `schema.d.ts` генерируется заново.

Старые имена не держатся: API и фронтенд выкатываются одним образом, других клиентов нет.

### 4. Настройки и миграция

- `IdeaTradingSettings.Risk` → `Amount`.
- По умолчанию `Default = (null, 20, 200, 5, 5, 1.5)`.
- Тело и ответ настроек: `amount` вместо `risk`, 400 по полю `amount`.

Миграция `202611070008` (`ReplaceIdeaTradingRiskWithAmountMigration`):
- в `idea_trading_settings` добавить `amount numeric NOT NULL DEFAULT 20`;
- снять default;
- удалить `risk`.

Сохранённая строка получает `20`. Перенос риска в сумму не делается: без цены и стопа риск в сумму не переводится.

Значение `20` выбрано мной, владелец его не задавал. Риск 1 USDT при стопе 5 % — это около 20 USDT. Владелец меняет
сумму в настройках.

### 5. Окно входа

- Поле «Сумма, USDT» вместо «Риск, USDT».
- Строки расчёта: «Цена входа», «Объём», «Сумма», «Убыток по стопу», «Прибыль по цели», «R:R после комиссий», «Свободные
  деньги».
- Сумма, убыток и прибыль показываются через `showDecimal(…, 2)`.
- Пояснение «Сумма упёрлась в предел» уходит вместе с флагом.

`SettingsPanel` переименовывает поле в «Сумма на сделку, USDT».

## Risks / Trade-offs

- [В настройках на проде после миграции стоит сумма 20, а не та, что владелец мог ждать] → Миграция пишет 20 у сохранённой
  строки. В отчёте о выкатке я напомню владельцу задать свою сумму.
- [С крупной суммой и далёким стопом убыток по стопу выходит больше, чем владелец задавал раньше риском] → Окно
  показывает убыток до входа, а «Не больше общего риска» по-прежнему отклоняет вход сверх предела.
- [Окно, открытое во время выкатки, шлёт `risk` в новый API] → `amount` в `POST /api/idea-trades` обязателен, поэтому
  старое тело получает 400, и сделка не создаётся. Расчёт без `amount` берёт сумму из настроек. После перезагрузки
  страницы окно работает как надо.
