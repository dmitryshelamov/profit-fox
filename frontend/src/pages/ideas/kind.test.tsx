import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { instrumentKindKey } from '@/modules/instrument-kind/useInstrumentKind';
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

/** The numbers of the row of strategies: 12 of the stocks, 40 of all. */
const counts: FakeRoute = (request) => {
  const ideas = new URL(request.url).searchParams.get('kind') === 'stock' ? 12 : 40;
  return json({
    total: ideas,
    strategies: [{ strategy: 'breakout-long', ideas }],
    filterGone: false,
  });
};

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([enabled]),
    'GET /api/ideas/state': () => json(scannerState({ closed: 3 })),
    'GET /api/ideas': ideaPage([idea()]),
    'GET /api/ideas/summary': () =>
      json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
    'GET /api/ideas/strategies': counts,
    ...extra,
  });
}

const queriesOf = (requests: readonly Request[], path: string) =>
  requests
    .map((request) => new URL(request.url))
    .filter((url) => url.pathname === path)
    .map((url) => url.searchParams);

const kindChoice = () => screen.getByRole('combobox', { name: 'Вид' });

// Change add-instrument-kind-filter, task 5.2: spec strategy-ideas «Раздел «Идеи»», the choice of the kind.
describe('the kind of a pair in «Идеи»', () => {
  // Scenario «Выбор вида».
  it('chooses the open ideas of a kind from the first page, and the row of strategies counts only them', async () => {
    const requests = routes();
    renderApp('/ideas?page=3');
    await screen.findByRole('button', { name: 'breakout-long 40' });
    const side = screen.getByRole('combobox', { name: 'Сторона' });
    expect(
      side.compareDocumentPosition(kindChoice()) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      within(kindChoice())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['Вид: все', 'крипта', 'акции']);
    expect(queriesOf(requests, '/api/ideas').at(-1)!.has('kind')).toBe(false);

    await userEvent.selectOptions(kindChoice(), 'stock');

    await waitFor(() => {
      const query = queriesOf(requests, '/api/ideas').at(-1)!;
      expect([query.get('kind'), query.get('page'), query.get('state')]).toEqual([
        'stock',
        '1',
        'open',
      ]);
    });
    expect(await screen.findByRole('button', { name: 'breakout-long 12' })).toBeInTheDocument();
    expect(queriesOf(requests, '/api/ideas/strategies').at(-1)!.get('kind')).toBe('stock');
  });

  // Scenario «Вид на обеих вкладках».
  it('keeps the kind of «Открытые» in «Журнал», for its summary, its table and its row of strategies', async () => {
    const requests = routes();
    renderApp('/ideas');
    await screen.findByRole('button', { name: 'breakout-long 40' });
    await userEvent.selectOptions(kindChoice(), 'crypto');

    await userEvent.click(screen.getByRole('tab', { name: /Журнал/ }));

    await screen.findByRole('table', { name: 'Сводка журнала' });
    expect(kindChoice()).toHaveValue('crypto');
    await waitFor(() => {
      for (const path of ['/api/ideas', '/api/ideas/summary', '/api/ideas/strategies']) {
        const query = queriesOf(requests, path).at(-1)!;
        expect([path, query.get('kind')]).toEqual([path, 'crypto']);
      }
    });
    expect(queriesOf(requests, '/api/ideas').at(-1)!.get('state')).toBe('closed');

    await userEvent.selectOptions(kindChoice(), '');
    await userEvent.click(screen.getByRole('tab', { name: /Открытые/ }));
    await screen.findByRole('table', { name: 'Открытые идеи' });
    expect(kindChoice()).toHaveValue('');
    await waitFor(() => expect(queriesOf(requests, '/api/ideas').at(-1)!.has('kind')).toBe(false));
  });

  // Scenario «Вид помнится».
  it('keeps the kind in the browser over a reload', async () => {
    routes();
    const first = renderApp('/ideas');
    await screen.findByRole('button', { name: 'breakout-long 40' });
    await userEvent.selectOptions(kindChoice(), 'crypto');
    first.unmount();

    const again = routes();
    renderApp('/ideas?tab=journal');
    await screen.findByRole('table', { name: 'Сводка журнала' });

    expect(kindChoice()).toHaveValue('crypto');
    expect(localStorage.getItem(instrumentKindKey)).toBe('crypto');
    expect(queriesOf(again, '/api/ideas/summary')[0]!.get('kind')).toBe('crypto');
  });
});
