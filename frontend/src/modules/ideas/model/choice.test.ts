import { loadIdeasChoice, saveIdeasChoice } from './choice';

const key = 'profit-fox.ideas.choice';

afterEach(() => localStorage.clear());

describe('the choice of the ideas screen in the browser', () => {
  const defaults = {
    filters: {
      setup: '',
      interval: '',
      type: '',
      side: '',
      category: '',
      nonNegativeExpectancy: false,
    },
    savedFilterId: null,
    sort: { column: 'expectancy', direction: 'desc' },
  };

  it('opens as before while nothing is stored', () => {
    expect(loadIdeasChoice()).toEqual(defaults);
  });

  // Scenario "Возврат на «Идеи»".
  it('gives back what was saved', () => {
    const choice = {
      filters: {
        setup: 'impulse',
        interval: '60',
        type: 'pullback',
        side: 'short',
        category: 'linear',
        nonNegativeExpectancy: true,
      },
      savedFilterId: 'f-1',
      sort: { column: 'riskReward', direction: 'asc' },
    } as const;

    saveIdeasChoice(choice);

    expect(loadIdeasChoice()).toEqual(choice);
  });

  it.each(['{broken', '5', '[]', 'null'])('ignores a stored value that is %s', (stored) => {
    localStorage.setItem(key, stored);

    expect(loadIdeasChoice()).toEqual(defaults);
  });

  it('drops the fields it does not know and the values that went stale, keeping the rest', () => {
    localStorage.setItem(
      key,
      JSON.stringify({
        filters: {
          side: 'up',
          interval: '60',
          setup: 'gone',
          nonNegativeExpectancy: 'yes',
          extra: 1,
        },
        savedFilterId: 5,
        sort: { column: 'nope', direction: 'desc' },
        extra: true,
      }),
    );

    expect(loadIdeasChoice()).toEqual({
      ...defaults,
      filters: { ...defaults.filters, interval: '60' },
    });
  });

  it('keeps the column of a sort whose direction is stale', () => {
    localStorage.setItem(key, JSON.stringify({ sort: { column: 'entry', direction: 'sideways' } }));

    expect(loadIdeasChoice().sort).toEqual({ column: 'entry', direction: 'desc' });
  });
});
