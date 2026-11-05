import { loadMarketChoice, saveMarketChoice } from './choice';

const key = 'profit-fox.market.choice';

afterEach(() => localStorage.clear());

describe('the choice of the market screen in the browser', () => {
  const defaults = {
    category: 'all',
    setup: 'all',
    setupInterval: '15',
    savedFilterId: null,
    sort: { column: 'change1h', direction: 'desc' },
  };

  it('opens as before while nothing is stored', () => {
    expect(loadMarketChoice()).toEqual(defaults);
  });

  // Scenario "Возврат на «Рынок»".
  it('gives back what was saved', () => {
    const choice = {
      category: 'linear',
      setup: 'impulse',
      setupInterval: '60',
      savedFilterId: 'f-1',
      sort: { column: 'change1h', direction: 'asc' },
    } as const;

    saveMarketChoice(choice);

    expect(loadMarketChoice()).toEqual(choice);
  });

  it('does not keep the search', () => {
    saveMarketChoice({ ...defaults, search: 'SOL' } as never);

    expect(localStorage.getItem(key)).not.toContain('SOL');
  });

  it.each(['{broken', '5', '[]', 'null'])('ignores a stored value that is %s', (stored) => {
    localStorage.setItem(key, stored);

    expect(loadMarketChoice()).toEqual(defaults);
  });

  it('drops the values that went stale, keeping the rest', () => {
    localStorage.setItem(
      key,
      JSON.stringify({
        category: 'inverse',
        setup: 'gone',
        setupInterval: '60',
        savedFilterId: 7,
        sort: { column: 'nope', direction: 'asc' },
      }),
    );

    expect(loadMarketChoice()).toEqual({ ...defaults, setupInterval: '60' });
  });
});
