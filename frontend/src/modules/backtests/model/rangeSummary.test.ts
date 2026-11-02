import { rangedNames, rangeSummary } from './rangeSummary';

describe('rangeSummary', () => {
  it('shows ranges as min…max / step in the order of the parameters', () => {
    expect(
      rangeSummary({
        fast: { min: 5, max: 15, step: 5 },
        slow: { min: 20, max: 40, step: 10 },
      }),
    ).toBe('fast 5…15 / 5 · slow 20…40 / 10');
  });

  it('shows duration ranges as given', () => {
    expect(rangeSummary({ cooldown: { min: '01:00:00', max: '04:00:00', step: '01:00:00' } })).toBe(
      'cooldown 01:00:00…04:00:00 / 01:00:00',
    );
  });

  it('skips single values', () => {
    expect(rangeSummary({ fast: 10, slow: { min: 20, max: 40, step: 10 } })).toBe(
      'slow 20…40 / 10',
    );
  });

  it('is empty when no parameter is a range', () => {
    expect(rangeSummary({ fast: 10 })).toBeUndefined();
  });
});

describe('rangedNames', () => {
  it('lists the parameters given as ranges in their order', () => {
    expect(
      rangedNames({
        fast: { min: 5, max: 15, step: 5 },
        takeProfit: 3.5,
        slow: { min: 1, max: 2, step: 1 },
      }),
    ).toEqual(['fast', 'slow']);
  });
});
