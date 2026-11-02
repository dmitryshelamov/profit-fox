import {
  addItem,
  COMPARISON_LIMIT,
  type ComparisonItem,
  formatItems,
  parseItems,
  removeItem,
  toggleItem,
} from './items';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const item = (n: number, combination = 0): ComparisonItem => ({
  backtestId: id(n),
  combination,
});
const filled = (count: number) => Array.from({ length: count }, (_, n) => item(n + 1));

describe('adding to the set', () => {
  it('adds a result', () => {
    expect(addItem([item(1)], item(2))).toEqual({ items: [item(1), item(2)], rejected: false });
  });

  it('keeps one entry for the same result', () => {
    expect(addItem([item(1, 4)], item(1, 4))).toEqual({ items: [item(1, 4)], rejected: false });
  });

  it('treats results of one backtest with different numbers as different', () => {
    expect(addItem([item(1, 4)], item(1, 5)).items).toHaveLength(2);
  });

  it('rejects the seventh result and reports it', () => {
    expect(COMPARISON_LIMIT).toBe(6);
    expect(addItem(filled(6), item(7))).toEqual({ items: filled(6), rejected: true });
  });
});

describe('toggling and removing', () => {
  it('toggles a result in', () => {
    expect(toggleItem([], item(1))).toEqual({ items: [item(1)], rejected: false });
  });

  it('toggles a present result out, even in a full set', () => {
    expect(toggleItem(filled(6), item(3))).toEqual({
      items: filled(6).filter((entry) => entry.backtestId !== id(3)),
      rejected: false,
    });
  });

  it('rejects toggling in a seventh result', () => {
    expect(toggleItem(filled(6), item(7)).rejected).toBe(true);
  });

  it('removes a result', () => {
    expect(removeItem([item(1), item(2, 3)], item(2, 3))).toEqual([item(1)]);
  });
});

describe('the address', () => {
  it('writes items as id:number pairs', () => {
    expect(formatItems([item(1, 4), item(2, 212)])).toBe(`${id(1)}:4,${id(2)}:212`);
  });

  it('reads them back', () => {
    expect(parseItems(`${id(1)}:4,${id(2)}:212`)).toEqual([item(1, 4), item(2, 212)]);
  });

  it('reads nothing from a missing value', () => {
    expect(parseItems(undefined)).toEqual([]);
  });

  it.each([
    ['an id that is not a uuid', 'abc:1'],
    ['a negative number', `${id(1)}:-1`],
    ['a fractional number', `${id(1)}:1.5`],
    ['no number', id(1)],
    ['an empty entry', ''],
  ])('skips %s', (_, entry) => {
    expect(parseItems(`${entry},${id(2)}:0`)).toEqual([item(2)]);
  });

  it('drops entries after the sixth', () => {
    expect(parseItems(formatItems(filled(8)))).toEqual(filled(6));
  });

  it('drops repeated entries', () => {
    expect(parseItems(`${id(1)}:0,${id(1)}:0,${id(2)}:0`)).toEqual([item(1), item(2)]);
  });
});
