import { isFinished, progressPercent, progressText, statusTitle } from './status';

const progress = {
  loadedCandles: 3120,
  expectedCandles: '4344',
  calculatedCombinations: 5,
  totalCombinations: '12',
};

describe('backtest status', () => {
  it.each([
    ['Pending', false],
    ['LoadingCandles', false],
    ['Running', false],
    ['Succeeded', true],
    ['Failed', true],
    ['Cancelled', true],
  ])('%s is finished: %s', (status, finished) => {
    expect(isFinished(status)).toBe(finished);
  });

  it.each([
    ['Pending', 'В очереди'],
    ['LoadingCandles', 'Загрузка свечей'],
    ['Running', 'Считается'],
    ['Succeeded', 'Готов'],
    ['Failed', 'Ошибка'],
    ['Cancelled', 'Отменён'],
  ])('titles %s as %s', (status, title) => {
    expect(statusTitle(status)).toBe(title);
  });

  it('titles a status it does not know as it is', () => {
    expect(statusTitle('Archived')).toBe('Archived');
  });

  it('shows loaded candles of expected while loading', () => {
    expect(progressText('LoadingCandles', progress)).toBe('3 120 / 4 344');
  });

  it.each(['Running', 'Cancelled'])('shows calculated combinations of all for %s', (status) => {
    expect(progressText(status, progress)).toBe('5 / 12');
  });

  it.each(['Pending', 'Succeeded', 'Failed'])('shows no progress for %s', (status) => {
    expect(progressText(status, progress)).toBeUndefined();
  });

  it('measures candles while loading and combinations while running, in whole percents', () => {
    expect(progressPercent('LoadingCandles', progress)).toBe(71);
    expect(progressPercent('Running', progress)).toBe(41);
    expect(progressPercent('Running', { ...progress, totalCombinations: 0 })).toBe(0);
    expect(progressPercent('Succeeded', progress)).toBeUndefined();
  });
});
