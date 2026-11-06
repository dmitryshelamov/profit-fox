import { loadJournalChoice, saveJournalChoice } from './choice';

const key = 'profit-fox.idea-journal.choice';

afterEach(() => localStorage.clear());

describe('the choice of the journal screen in the browser', () => {
  const defaults = {
    filters: { setup: '', symbol: '', side: '', badge: '', days: '', origin: '', autoId: '' },
    pageSize: 50,
  };

  it('opens with every trade and the first size while nothing is stored', () => {
    expect(loadJournalChoice()).toEqual(defaults);
  });

  // Scenario "Возврат на экран".
  it('gives back what was saved', () => {
    const choice = {
      filters: {
        setup: 'drop',
        symbol: 'eth',
        side: 'short',
        badge: 'none',
        days: '30',
        origin: 'paper',
        autoId: 'auto-1',
      },
      pageSize: 100,
    } as const;

    saveJournalChoice(choice);

    expect(loadJournalChoice()).toEqual(choice);
  });

  it('takes what is no longer known, or broken, as nothing chosen', () => {
    localStorage.setItem(
      key,
      JSON.stringify({
        filters: {
          setup: 'gone',
          symbol: 5,
          side: 'up',
          badge: 'big',
          days: '14',
          origin: 'robot',
          autoId: 7,
        },
        pageSize: 75,
      }),
    );

    expect(loadJournalChoice()).toEqual(defaults);

    localStorage.setItem(key, JSON.stringify({ filters: 5 }));

    expect(loadJournalChoice()).toEqual(defaults);

    localStorage.setItem(key, '{broken');

    expect(loadJournalChoice()).toEqual(defaults);
  });
});
