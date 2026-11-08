# Spec Delta

## ADDED Requirements

### Requirement: Счётчик запросов к T-Invest

Каждый унарный gRPC-вызов к T-Invest SHALL увеличивать счётчик `profitfox.tinvest.requests` ровно на 1, с подписями:
группа методов (`instruments`, `market_data`, `users`, `operations`, `orders`), метод и исход (`ok`, `rate_limited`,
`error`). Вызов, который лимитер не выпустил из-за паузы после `RESOURCE_EXHAUSTED`, MUST NOT считаться запросом.

#### Scenario: Чтение свечей

- **WHEN** сервис вызывает `MarketDataService/GetCandles` и получает ответ
- **THEN** счётчик с группой `market_data`, методом `GetCandles` и исходом `ok` вырастает на 1

#### Scenario: Лимит токена

- **WHEN** T-Invest отвечает на `GetCandles` статусом `RESOURCE_EXHAUSTED`
- **THEN** счётчик с исходом `rate_limited` вырастает на 1

### Requirement: Остаток лимита T-Invest по заголовкам

Для каждой группы методов, в ответах которой были заголовки `x-ratelimit-limit` и `x-ratelimit-remaining`, система SHALL
отдавать две метрики: последний лимит и наименьший остаток за период экспорта. Подпись — только группа методов. Если за
период таких ответов не было, остаток за этот период MUST NOT отдаваться.

#### Scenario: Остаток группы market_data

- **WHEN** за период экспорта ответы `market_data` несли остатки `250`, `120` и `200` при лимите `300`
- **THEN** метрики отдают лимит `300` и остаток `120` с подписью `market_data`
