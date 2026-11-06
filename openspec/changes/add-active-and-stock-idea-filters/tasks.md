# Tasks

Новая логика — строго red → green → refactor: сначала тест, он красный по ожидаемой причине, затем минимум кода.
Прод в этом изменении не трогаем: выкладка — только с разрешения владельца.

## 1. Признак некриптовой пары

- [x] 1.1 `ListedInstrument.TradFi` и перевод `SymbolType` в `BybitMarketDataClient.RequestListAsync` для `spot` и
  `linear`: `stock`, `xstocks`, `ETF`, `commodity`, `forex` → `true`; `innovation`, `adventure`, пустой → `false`.
  Проверка: юнит-тест разбора ответа `instruments-info` в `Tests.Unit/Infrastructure/Exchanges/Bybit` на сценарии
  спеки «Перпетуал на акцию», «Спотовый xStock», «Сырьё, форекс и ETF», «Монета зоны Innovation».
- [x] 1.2 `ScannerPair.TradFi`; `ScannerUniverse.Merge` берёт признак из списка у новой пары и у известной пары в
  списке, изменившийся признак делает пару изменившейся. Проверка: тесты `ScannerUniverseTests` — сценарий «Признак у
  известной пары» и новая некриптовая пара; прежние тесты проходят.
- [x] 1.3 Миграция `scanner_pairs.tradfi boolean NOT NULL DEFAULT false`, `ScannerPairDb`, конвертер и SQL
  репозитория. Проверка: `ScannerPairRepositoryTests` — некриптовая пара читается обратно с признаком.
- [x] 1.4 Термин «Некриптовая пара (TradFi)» в `CONTEXT.md`. Проверка: определение совпадает с требованием
  «Некриптовые пары сканера».

## 2. Фильтры в чтении идей

- [x] 2.1 `GetIdeasQuery.ActiveOnly`: только `AwaitingEntry` и `Active`. Проверка: `GetIdeasTests` — сценарий «Только
  активные» (число идей `2`).
- [x] 2.2 `GetIdeasQuery.HideTradFi`: обработчик получает `IScannerPairRepository`, читает пары только при флаге,
  убирает идеи некриптовых пар; идея пары, которой нет в хранилище, остаётся. Проверка: `GetIdeasTests` — сценарий
  «Скрыть акции», пара не из хранилища, и тест, что без флага пары не читаются.
- [x] 2.3 `IdeasQueryParameters.ActiveOnly` и `HideStocks`, передача в запрос, описания для OpenAPI. Проверка:
  `IdeasApiTests` — кейсы `ActiveOnly=true` и `HideStocks=true` в тесте фильтров по полям и сценарий «Вместе с другим
  фильтром»; без флагов ответ прежний (прежние тесты проходят).

## 3. Экран «Идеи»

- [x] 3.1 Перегенерировать типы API фронтенда из OpenAPI. Проверка: `npm run typecheck` проходит, в схеме есть оба
  параметра.
- [x] 3.2 `IdeaFilters.activeOnly` и `hideStocks`, `noIdeaFilters`, `ideasQuery` передаёт `ActiveOnly` / `HideStocks`
  только при `true`; `loadIdeasChoice` читает оба флага. Проверка: `choice.test.ts` — сценарии «Переключатели после
  перезагрузки» и «Выбор, сохранённый до изменения».
- [x] 3.3 Чекбоксы «Только активные» и «Скрыть акции» (подсказка «акции, xStocks, ETF, сырьё и форекс») рядом с
  «Ожидание ≥ 0 R»; смена возвращает на первую страницу. Проверка: в браузере на локальном dev — переключатели
  меняют список и число идей, выбор переживает перезагрузку, в консоли нет ошибок.

## 4. Проверка целиком

- [x] 4.1 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; во `frontend/` — lint, format check,
  typecheck, тесты и сборка. Проверка: всё зелёное.
- [x] 4.2 Пройти дифф: каждое новое условие и ветка требуются тестом. Проверка: нет кода без теста.

## Workflow follow-up

- `/opsx:archive` перед PR, затем PR в `main` со squash.
- Выкладка на прод — по разрешению владельца; после старта сканера проверить, что «Скрыть акции» убирает `AAPLUSDT`.
