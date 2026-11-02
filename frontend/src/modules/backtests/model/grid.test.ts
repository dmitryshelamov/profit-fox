import { combinationsOf, rangeCount } from './grid';

describe('count of values of a range', () => {
  it('counts numbers with a fractional step', () => {
    expect(rangeCount({ min: '5', max: '20', step: '5' })).toEqual({ count: 4n });
    expect(rangeCount({ min: '0.1', max: '0.5', step: '0.1' })).toEqual({ count: 5n });
  });

  it('counts a single value', () => {
    expect(rangeCount({ min: '3', max: '3', step: '1' })).toEqual({ count: 1n });
  });

  it('counts durations', () => {
    expect(rangeCount({ min: '01:00:00', max: '04:00:00', step: '01:00:00' })).toEqual({
      count: 4n,
    });
    expect(rangeCount({ min: '00:30:00', max: '1.02:00:00', step: '00:30:00' })).toEqual({
      count: 52n,
    });
  });

  it('counts beyond 2^53 without losing precision', () => {
    expect(rangeCount({ min: '0', max: '9007199254740993', step: '1' })).toEqual({
      count: 9007199254740994n,
    });
  });

  it('does not decompose a range with a step not above zero', () => {
    expect(rangeCount({ min: '1', max: '5', step: '0' })).toEqual({ problem: 'step' });
    expect(rangeCount({ min: '1', max: '5', step: '-1' })).toEqual({ problem: 'step' });
  });

  it('does not decompose a range with min above max', () => {
    expect(rangeCount({ min: '6', max: '5', step: '1' })).toEqual({ problem: 'order' });
  });

  it('does not decompose a range whose step does not divide it', () => {
    expect(rangeCount({ min: '1', max: '10', step: '4' })).toEqual({ problem: 'divisibility' });
  });
});

describe('combinations', () => {
  it('multiplies the counts of two grids', () => {
    expect(combinationsOf([4n, 5n])).toBe(20n);
  });

  it('is one when nothing is a range', () => {
    expect(combinationsOf([])).toBe(1n);
  });

  it('multiplies beyond 2^53 exactly', () => {
    expect(combinationsOf([9007199254740993n, 3n])).toBe(27021597764222979n);
  });
});
