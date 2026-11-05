import { setupIntervals, setupLabel } from './setups';

describe('labels', () => {
  it('names the setups and the intervals', () => {
    expect(setupLabel('early_drop')).toBe('Раннее падение');
    expect(setupIntervals.map((interval) => interval.label)).toEqual([
      '5 мин',
      '15 мин',
      '1 ч',
      '4 ч',
    ]);
  });

  it('shows an unknown setup as its code', () => {
    expect(setupLabel('mystery')).toBe('mystery');
  });
});
