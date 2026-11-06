# Tasks

## 1. Домен

- [x] 1.1 `IdeaAuto.Budget`, `IdeaTradingSettings.AutosBudget`, причины `IdeaTradeRefusal.BudgetTaken` и `AutosBudgetTaken`; проверка — сборка и существующие тесты зелёные
- [x] 1.2 `Services/IdeaAutos/AutoBudget.Used` и `AutosUsed` по TDD на эталонном наборе (реальные «Ждёт входа» и «Открыта» на любом аккаунте, учебные, закрытые, отменённые, ручные, чужие автоматы, удалённый автомат); проверка — `AutoBudgetTests`
- [x] 1.3 `AutoRefusals.BudgetTaken` и бюджеты в `AutoRefusals.Check` (порядок причин, ровно в бюджет, без бюджета, бюджет всех автоматов не видит ручных); проверка — `AutoRefusalsTests`
- [x] 1.4 Бюджет автомата в `PaperEntry.Check`, без бюджета всех автоматов; проверка — `PaperEntryTests`

## 2. Хранение и API

- [ ] 2.1 Миграция `202611080002` `AddIdeaAutoBudgetsMigration`, чтение и запись `budget` и `autos_budget` в репозиториях; проверка — тест миграции и интеграционные тесты репозиториев
- [ ] 2.2 `budget` во входе и ответе автомата, 400 по `budget` при бюджете меньше суммы; проверка — `IdeaAutosApiTests` и тесты `IdeaAutoValidation`
- [ ] 2.3 `budgetUsed` в списке автоматов (`IdeaAutoSummary`); проверка — тест `GetIdeaAutos` и `IdeaAutosApiTests`
- [ ] 2.4 `autosBudget` в настройках, 400 при значении не больше нуля; проверка — тесты `SaveIdeaTradingSettings` и `IdeaTradingSettingsApiTests`
- [ ] 2.5 `autosBudgetUsed` и `autosBudget` в `GET /api/idea-trading/guards`; проверка — тест `GetIdeaGuards` и API-тест
- [ ] 2.6 Коды `budget_taken` и `autos_budget_taken` в `IdeaTradeCodes`; проверка — тест кодов

## 3. Сканер и Worker

- [ ] 3.1 Две учебные идеи одного автомата в одной минуте: вторая получает «Бюджет автомата занят»; проверка — `LiveAutosTests`
- [ ] 3.2 Вход с деньгами передаёт сумму заявки и бюджет всех автоматов в `AutoRefusals.Check`; проверка — `IdeaTradeEntryCalculatorTests` и `RunNextAutoEntryTests`
- [ ] 3.3 Интеграционный тест Worker'а на `FakeExchange`: две заявки одного автомата при бюджете на одну и отказ по бюджету всех автоматов; проверка — `IdeaAutoEntryTests`

## 4. Фронтенд

- [ ] 4.1 Перегенерировать `schema.d.ts`; проверка — `npm run typecheck`
- [ ] 4.2 Тексты причин и подробностей по бюджетам, новые слова причин `amount_above_max`, `total_risk_exceeded`, `risk_reward_too_low`; проверка — `reasons.test.ts`, `labels.test.ts`, `refusal.test.ts`
- [ ] 4.3 Бюджет в форме автомата (`autoForm`, `AutoForm`) и колонка «Бюджет» в списке (`totals.budgetText`); проверка — тесты модели и экранов автоматов
- [ ] 4.4 Строка защит и экран настроек: бюджет всех автоматов, новые подписи и подсказки; проверка — `guards.test.ts`, `totals.test.ts`, `ideaTradingSettings.test.tsx`
- [ ] 4.5 e2e `idea-autos.spec.ts` на заглушке Bybit: занятый бюджет в списке, отказ «Бюджет автомата занят», бюджет автоматов в строке защит; проверка — `npm run test:e2e` (или CI)

## 5. Проверка целиком

- [ ] 5.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; во `frontend/` — `npm run lint`, `format:check`, `typecheck`, `test`, `build`; что не запускалось здесь — названо в PR
- [ ] 5.2 Обновить `docs/conventions.md` (`AutoBudget`, новые поля и причины); проверка — описание совпадает с кодом

## Workflow follow-up

- `/opsx:archive add-idea-auto-budget` с синхронизацией спецификаций, коммит `docs(idea-trades): archive add-idea-auto-budget`.
- PR `feat(idea-trades): add the budgets of autos and clearer trading settings`.
