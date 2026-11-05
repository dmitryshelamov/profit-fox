# Tasks

Каждая задача с новым поведением — строго red → green → refactor: сначала тест, он падает по ожидаемой причине, потом
минимальный код.

## 1. Признак бана IP

- [x] 1.1 В `BybitPublicDataClientTests` (через `FakeBybitHandler`) тест: HTTP 403 даёт паузу `RestPause`, а `10006` — нет
      (пока red только для второй части); затем `TransientExchangeException.IpBanned` и его установка в `Unwrap` только
      для 403. Проверка: тест зелёный, прежние тесты `Unwrap`/шлюза зелёные.

## 2. Лестница коротких пауз в `ScannerRestGate`

- [x] 2.1 `ScannerSettings.RestLimitPause` (по умолчанию 30 секунд) и тест в `ScannerRestGateTests`: на `10006` запросы не
      уходят `RestLimitPause`, состояние получает конец этой паузы. Проверка: тест сначала red, потом green.
- [x] 2.2 Тест: `10006` подряд даёт паузы 30 с, 1, 2, 4, 8 минут, дальше `RestPause` (10 минут) — сценарий «несколько раз
      подряд». Проверка: red → green.
- [x] 2.3 Тест: успешный запрос после паузы возвращает следующую паузу на `10006` к `RestLimitPause` — сценарий «Успешный
      ответ после 10006». Проверка: red → green.
- [x] 2.4 Тест: 403 даёт `RestPause` и после серии `10006` (счётчик не влияет) — существующий сценарий «Bybit ответил 403»
      остаётся зелёным, новый тест red → green.
- [x] 2.5 `RestLimitPause` в `backend/src/ProfitFox.Scanner/appsettings.json` и в примере секции `Scanner` в
      `docs/conventions.md`; там же описание шлюза: 403 — `RestPause`, `10006`/429 — лестница от `RestLimitPause` до
      `RestPause`. Проверка: документ совпадает с кодом и `appsettings.json`.

## 3. Проверка целиком

- [x] 3.1 Пройти diff: каждая ветка и условие нового кода требуются каким-то тестом, иначе удалить или дописать тест.
- [x] 3.2 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` и
      `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят.

## Workflow follow-up

- `/opsx:archive` перед pull request.
- Pull request `fix(scanner): shorten the rest pause on bybit rate limit`; после деплоя проверить по логам сканера, что
  после `10006` в начале четверти часа загрузка продолжается через 30 секунд.
