import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { minScoreKey } from '@/pages/ideas/useMinScore';
import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea, ideaPage, scannerState } from '@/test/ideas';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const enabled = {
  name: 'breakout-long',
  description: null,
  version: 7,
  error: null,
  signals: [],
  exits: [],
  kind: 'idea',
  ideas: { enabled: true, timeframes: ['1h'] },
};

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([enabled]),
    'GET /api/ideas/state': () => json(scannerState({ closed: 1 })),
    'GET /api/ideas': ideaPage([idea({ score: 2 })]),
    'GET /api/ideas/summary': () =>
      json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
    ...extra,
  });
}

const queriesOf = (requests: readonly Request[], path: string) =>
  requests
    .map((request) => new URL(request.url))
    .filter((url) => url.pathname === path)
    .map((url) => url.searchParams);

const scoreCell = (row: HTMLElement) => {
  const table = row.closest('table')!;
  const heads = within(table)
    .getAllByRole('columnheader')
    .map((head) => head.textContent?.replace(/[↕↑↓]$/, ''));
  return within(row).getAllByRole('cell')[heads.indexOf('Балл')]!;
};

// Change add-idea-score, task 3.5: spec idea-score «Балл в разделе «Идеи»».
describe('the score in «Идеи»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Колонка балла».
  it('shows the score of an open idea with its sign, green above nought', async () => {
    routes();
    renderApp('/ideas');

    const cell = scoreCell(await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' }));
    expect(cell).toHaveTextContent('+2');
    expect(within(cell).getByText('+2')).toHaveClass('text-ok');
  });

  it('shows a score below nought in red and nought in grey', async () => {
    routes({
      'GET /api/ideas': ideaPage([
        idea({ id: 'a', symbol: 'AUSDT', score: -1 }),
        idea({ id: 'b', symbol: 'BUSDT', score: 0 }),
      ]),
    });
    renderApp('/ideas');

    const minus = scoreCell(await screen.findByRole('row', { name: 'AUSDT breakout-long 1h' }));
    const nought = scoreCell(screen.getByRole('row', { name: 'BUSDT breakout-long 1h' }));
    expect(within(minus).getByText('-1')).toHaveClass('text-err');
    expect(within(nought).getByText('0')).toHaveClass('text-muted');
  });

  // Scenario «Сортировка по колонке».
  it('sorts the journal by the score from the highest on the first page', async () => {
    const requests = routes({
      'GET /api/ideas': ideaPage([idea({ state: 'closed', result: 0.01, score: 1 })]),
    });
    const { history } = renderApp('/ideas?tab=journal&page=2');
    const closed = await screen.findByRole('table', { name: 'Закрытые идеи' });

    await userEvent.click(within(closed).getByRole('button', { name: /Балл/ }));

    await waitFor(() => {
      const query = queriesOf(requests, '/api/ideas').at(-1)!;
      expect([
        query.get('state'),
        query.get('sort'),
        query.get('direction'),
        query.get('page'),
      ]).toEqual(['closed', 'score', 'desc', '1']);
    });
    expect(history.location.search).not.toContain('page=');
  });

  // Scenario «Выбор «Балл от»».
  it('takes «Балл от 2» on the open ideas into the journal and its summary, and the browser keeps it', async () => {
    const requests = routes();
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Балл от' }), '2');
    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas').at(-1)!.get('minScore')).toBe('2'),
    );
    await userEvent.click(screen.getByRole('tab', { name: /Журнал/ }));

    await waitFor(() => {
      const journal = queriesOf(requests, '/api/ideas').at(-1)!;
      expect([journal.get('state'), journal.get('minScore')]).toEqual(['closed', '2']);
    });
    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas/summary').at(-1)!.get('minScore')).toBe('2'),
    );
    expect(screen.getByRole('combobox', { name: 'Балл от' })).toHaveValue('2');
    expect(localStorage.getItem(minScoreKey)).toBe('2');
  });

  it('starts with the score the browser keeps, and with none without it', async () => {
    localStorage.setItem(minScoreKey, '-1');
    const requests = routes();
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });

    expect(screen.getByRole('combobox', { name: 'Балл от' })).toHaveValue('-1');
    expect(queriesOf(requests, '/api/ideas').at(-1)!.get('minScore')).toBe('-1');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Балл от' }), '');

    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas').at(-1)!.has('minScore')).toBe(false),
    );
    expect(localStorage.getItem(minScoreKey)).toBeNull();
  });

  it('offers the scores from −3 to +9', async () => {
    routes();
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });

    const options = within(screen.getByRole('combobox', { name: 'Балл от' }))
      .getAllByRole('option')
      .map((option) => option.textContent);
    expect(options[0]).toBe('Балл от: все');
    expect(options.slice(1)).toEqual([
      'Балл от: -3',
      'Балл от: -2',
      'Балл от: -1',
      'Балл от: 0',
      'Балл от: +1',
      'Балл от: +2',
      'Балл от: +3',
      'Балл от: +4',
      'Балл от: +5',
      'Балл от: +6',
      'Балл от: +7',
      'Балл от: +8',
      'Балл от: +9',
    ]);
  });

  it('counts the strategies with «Балл от»', async () => {
    const requests = routes({
      'GET /api/ideas/strategies': () =>
        json({
          total: 1,
          strategies: [{ strategy: 'breakout-long', ideas: 1 }],
          filterGone: false,
        }),
    });
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Балл от' }), '3');

    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas/strategies').at(-1)!.get('minScore')).toBe('3'),
    );
  });
});

// Change label-score-hint, spec idea-labels «Подсказка меток с весами»: the hint of the labels shows what each adds.
describe('the hint of the labels of an idea', () => {
  it('shows the score of the side, the weighted labels by what they add, and the rest in one line', async () => {
    routes({
      'GET /api/ideas': ideaPage([
        idea({
          side: 'long',
          score: 1,
          labels: ['OI ↓ 4h', 'волат.↑ 15m', 'цена ↓ 4h', '↓ BTC 1h'],
        }),
      ]),
      'GET /api/scoring/labels': () =>
        json([
          {
            text: 'цена ↓ 4h',
            enabled: true,
            timeframe: '4h',
            signal: 'price-down-4h',
            longWeight: 1,
            shortWeight: -1,
            builtIn: false,
          },
          {
            text: '↓ BTC 1h',
            enabled: true,
            timeframe: '1h',
            signal: 'btc-down-1h',
            longWeight: -1,
            shortWeight: 1,
            builtIn: false,
          },
          {
            text: 'OI ↓ 4h',
            enabled: true,
            timeframe: '4h',
            signal: 'oi-down-4h',
            longWeight: 1,
            shortWeight: 0,
            builtIn: false,
          },
          {
            text: 'волат.↑ 15m',
            enabled: true,
            timeframe: '15m',
            signal: 'vol-high-15m',
            longWeight: 0,
            shortWeight: 0,
            builtIn: false,
          },
        ]),
    });
    renderApp('/ideas');
    const row = await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    const badges = within(row).getByText('OI ↓ 4h').parentElement!;
    await userEvent.hover(badges);
    const hint = await screen.findByRole('tooltip');
    await waitFor(() => expect(hint).toHaveTextContent('Балл лонга+1'));
    expect(
      within(hint)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['OI ↓ 4h+1', 'цена ↓ 4h+1', '↓ BTC 1h-1']);
    expect(hint).toHaveTextContent('без веса (1): волат.↑ 15m');
  });
});
