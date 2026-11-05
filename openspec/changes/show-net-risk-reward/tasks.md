# Tasks

Новая логика идёт строго по TDD: красный тест, минимальный код, рефакторинг.

## 1. Backend

- [x] 1.1 `IdeaShares.NetRiskReward` — прибыль на 1 USDT, делённая на убыток на 1 USDT. Готово, когда NUnit на примере
  из спеки (вход 100, стоп 98, цель 104, `linear` по рынку) даёт `0.038878 / 0.021089`, а пример со стопом 43.78 при
  входе 44.06 даёт 1.56 с округлением до двух знаков.
- [x] 1.2 `IdeaOrder`: колонка `riskReward` сортирует по `Shares.NetRiskReward`. Готово, когда тест «Сортировка по R:R
  после комиссий» зелёный, а прежний тест колонки обновлён под новое значение.
- [x] 1.3 `SavedFilterMatch.Idea`: условие `riskReward` сравнивает `Shares.NetRiskReward`. Готово, когда тест «Условие
  R:R в фильтре» зелёный.
- [ ] 1.4 `IdeaResponse.NetRiskReward`: десятичная строка рядом с `RiskReward`. Готово, когда интеграционный тест
  `GET /api/ideas` видит поле у идеи из примера спеки.
- [ ] 1.5 Своя идея: `GetOwnIdeaQueryHandler` берёт `BacktestSettings`, считает доли по комиссиям категории,
  `GetOwnIdeaResult` и `OwnIdeaResponse` получают `NetRiskReward`. Готово, когда unit-тест обработчика и интеграционный
  тест `POST .../own-idea` видят поле.
- [ ] 1.6 Обновить комментарии контрактов и `docs/conventions.md`, где описан R:R; перегенерировать `schema.d.ts` через
  `npm run generate:api`. Готово, когда typecheck видит `netRiskReward`.

## 2. Frontend

- [ ] 2.1 `RiskRewardCell` в `ideas/ui/IdeaCells.tsx`: R:R после комиссий с двумя знаками в плашке (`ok` от 1, `err` ниже
  1) и строка «без ком. X.XX». Готово, когда тесты Vitest проверяют `1.56`, `0.95`, отрицательное значение и подпись.
- [ ] 2.2 Колонка «R:R» в `IdeasTable`, карточка в `PairIdeasList` и `OwnIdeaBlock` используют `RiskRewardCell`. Готово,
  когда тесты экранов видят `1.84` и «без ком. 2.00» у идеи из примера спеки.

## 3. Проверка

- [ ] 3.1 Пройти дифф: каждая новая ветка требуется тестом. Затем полный прогон:
  - `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`;
  - фронт: lint, format check, typecheck, тесты, сборка;
  - `npm run test:e2e`.

  Готово, когда всё зелёное.

## Workflow follow-up

- `/opsx:archive`, затем PR `feat(ideas): show reward to risk after fees`.
