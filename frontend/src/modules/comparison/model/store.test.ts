import {
  clearComparison,
  comparisonRejected,
  comparisonSnapshot,
  removeFromComparison,
  replaceComparison,
  subscribeComparison,
  toggleComparison,
} from './store';

const id = (n: number) => `${String(n).repeat(8)}-0000-4000-8000-000000000000`;
const item = (n: number, combination = 0) => ({ backtestId: id(n), combination });

describe('comparison store', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps the set in the tab storage under one key', () => {
    toggleComparison(item(1, 4));
    toggleComparison(item(2, 212));

    expect(sessionStorage.getItem('profit-fox-comparison')).toBe(`${id(1)}:4,${id(2)}:212`);
    expect(comparisonSnapshot()).toEqual([item(1, 4), item(2, 212)]);
  });

  it('gives the same snapshot until the set changes', () => {
    toggleComparison(item(1));
    const first = comparisonSnapshot();

    expect(comparisonSnapshot()).toBe(first);
    toggleComparison(item(2));
    expect(comparisonSnapshot()).not.toBe(first);
  });

  it('removes a result that is marked again', () => {
    toggleComparison(item(1));
    toggleComparison(item(1));

    expect(comparisonSnapshot()).toEqual([]);
  });

  it('refuses the seventh result and says so', () => {
    for (let n = 1; n <= 6; n++) {
      toggleComparison(item(n));
    }

    expect(toggleComparison(item(7)).rejected).toBe(true);
    expect(comparisonSnapshot()).toHaveLength(6);
  });

  it('remembers that the last result was refused until the set changes', () => {
    for (let n = 1; n <= 6; n++) {
      toggleComparison(item(n));
    }
    expect(comparisonRejected()).toBe(false);

    toggleComparison(item(7));
    expect(comparisonRejected()).toBe(true);

    toggleComparison(item(1));
    expect(comparisonRejected()).toBe(false);
  });

  it('removes one result and clears the set', () => {
    toggleComparison(item(1));
    toggleComparison(item(2));
    removeFromComparison(item(1));
    expect(comparisonSnapshot()).toEqual([item(2)]);

    clearComparison();
    expect(comparisonSnapshot()).toEqual([]);
  });

  it('replaces the set', () => {
    toggleComparison(item(1));
    replaceComparison([item(3), item(4)]);

    expect(comparisonSnapshot()).toEqual([item(3), item(4)]);
  });

  it('tells subscribers about a change and stops after unsubscribing', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeComparison(listener);

    toggleComparison(item(1));
    expect(listener).toHaveBeenCalledTimes(1);

    unsubscribe();
    toggleComparison(item(2));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('works from memory when the tab storage is not available', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    toggleComparison(item(1));

    expect(comparisonSnapshot()).toEqual([item(1)]);
  });
});
