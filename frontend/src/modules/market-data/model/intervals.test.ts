import { intervalLabel, intervals, intervalsOf, marketCategories } from './intervals';

describe('intervals', () => {
  it('lists the Bybit intervals in ascending order', () => {
    expect(intervals.map((interval) => interval.value)).toEqual([
      '1',
      '3',
      '5',
      '15',
      '30',
      '60',
      '120',
      '240',
      '360',
      '720',
      'D',
      'W',
      'M',
    ]);
  });

  // Scenario "Интервалы MOEX": the intervals are a property of the category.
  it.each(['spot', 'linear'] as const)('gives %s every interval of Bybit', (category) => {
    expect(intervalsOf(category)).toEqual(intervals);
  });

  it('gives moex the eight intervals up to four hours, without D', () => {
    expect(intervalsOf('moex').map((interval) => interval.value)).toEqual([
      '1',
      '3',
      '5',
      '15',
      '30',
      '60',
      '120',
      '240',
    ]);
  });

  it('names the three market categories', () => {
    expect(marketCategories).toEqual(['spot', 'linear', 'moex']);
  });

  it.each([
    ['1', '1 мин'],
    ['30', '30 мин'],
    ['60', '1 ч'],
    ['240', '4 ч'],
    ['720', '12 ч'],
    ['D', '1 д'],
    ['W', '1 нед'],
    ['M', '1 мес'],
  ])('labels %s as %s', (value, label) => {
    expect(intervalLabel(value)).toBe(label);
  });

  it('shows an unknown interval as it is', () => {
    expect(intervalLabel('7')).toBe('7');
  });
});
