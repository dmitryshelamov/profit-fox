import { pairIntervals, shownPairInterval } from './pairIntervals';

describe('pair intervals', () => {
  it('offers 1 min, 5 min, 15 min, 1 h, 4 h and 1 d', () => {
    expect(pairIntervals).toEqual([
      { value: '1', label: '1 мин' },
      { value: '5', label: '5 мин' },
      { value: '15', label: '15 мин' },
      { value: '60', label: '1 ч' },
      { value: '240', label: '4 ч' },
      { value: 'D', label: '1 д' },
    ]);
  });

  it('shows the interval of the address when it is offered, otherwise 15 min', () => {
    expect(shownPairInterval('240')).toBe('240');
    expect(shownPairInterval('D')).toBe('D');
    expect(shownPairInterval('3')).toBe('15');
    expect(shownPairInterval(undefined)).toBe('15');
  });
});
