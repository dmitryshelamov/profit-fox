# Tasks

## 1. Domain

- [x] 1.1 Тест «разбивка по ТФ»: строки только проверенных ТФ в порядке перечисления, `Total` и `Up` по скученным суммам случаев строки; затем `CalibrationSlice` и `CalibrationReport.Intervals` — тест `CalibrationCalculatorTests` зелёный
- [x] 1.2 Тест «строка без ▲» (нет `Up`) — зелёный
- [x] 1.3 Тест «сравнение в строке» на случаях, которые проверяет старая модель — зелёный
- [x] 1.4 Тест «разбивка по типам» и `CalibrationReport.Types` — зелёный
- [x] 1.5 Тест: разбивки итога `fresh` по `linear` за 30 дней только по его проверенным случаям — зелёный
- [x] 1.6 Описать `CalibrationSlice` в `docs/conventions.md`

## 2. Storage

- [x] 2.1 Интеграционный тест `IdeaCalibrationRepositoryTests`: разбивки сохраняются и читаются; `report` без разбивок читается с пустыми — затем `CalibrationSliceDb` и конвертер

## 3. API

- [x] 3.1 Тест `IdeasApiTests`: ответ с `intervals` и `types` (коды ТФ и типов, строки, сравнение), пустые до расчёта — затем `CalibrationSliceResponse`
- [x] 3.2 Обновить OpenAPI-схему фронтенда (`schema.d.ts`)

## 4. Frontend

- [x] 4.1 Тест страницы: таблицы «По ТФ» и «По типам» и их сравнения; без разбивок их нет — затем компоненты страницы
- [x] 4.2 lint, format:check, typecheck, test, build фронтенда проходят

## 5. Проверка

- [x] 5.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` проходят

## Workflow follow-up

- Архивировать изменение (`/opsx:archive`) в той же ветке.
