import { intervalLabel, intervals } from './intervals';

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
