# Spec Delta

## ADDED Requirements

### Requirement: Счётчик ликвидаций

Сканер SHALL увеличивать счётчик `profitfox.scanner.liquidations` на 1 за каждое сохранённое событие ликвидации, с
меткой `side` (`long` или `short`). Событие, которое уже было в хранилище, MUST NOT увеличивать счётчик.

#### Scenario: Пачка событий записана

- **WHEN** сканер записал пачку из 3 новых ликвидаций лонгов и 1 шорта
- **THEN** счётчик `profitfox.scanner.liquidations` вырос на 3 с `side=long` и на 1 с `side=short`

#### Scenario: Повтор не считается

- **WHEN** в пачке одно событие уже есть в хранилище
- **THEN** счётчик не растёт на него
