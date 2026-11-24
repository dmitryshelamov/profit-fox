# Spec Delta

## MODIFIED Requirements

### Requirement: Команды CLI

CLI `bench` SHALL быть клиентом API своего стенда и давать поставить прогон стратегии (`run`), увидеть очередь и
последние прогоны (`runs`) и отменить прогон (`cancel`). Команда `run` SHALL принимать имя стратегии и ключи версии,
таймфреймов, значений параметров, начала и конца истории и пар. Адрес стенда SHALL браться из настройки, по умолчанию
`http://localhost:5240`, и ключа `--bench`.

#### Scenario: Прогон с ожиданием

- **WHEN** запускают `bench run funding-short --timeframe 4h --wait`
- **THEN** команда ждёт конца прогона, печатает числа наборов и выходит с кодом 0

#### Scenario: Прогон без ожидания

- **WHEN** запускают `bench run funding-short --timeframe 4h`
- **THEN** команда печатает id поставленного прогона и сразу выходит с кодом 0

#### Scenario: Отказ при постановке

- **WHEN** запускают `bench run funding-short --timeframe 2m`
- **THEN** команда печатает причину отказа и выходит с ненулевым кодом

#### Scenario: Прогон упал

- **WHEN** прогон, запущенный `bench run funding-short --wait`, закончился `failed` или `cancelled`
- **THEN** команда печатает состояние и причину и выходит с ненулевым кодом

#### Scenario: Прогон на 5m

- **WHEN** запускают `bench run funding-short --timeframe 5m`
- **THEN** команда печатает id поставленного прогона и выходит с кодом 0
