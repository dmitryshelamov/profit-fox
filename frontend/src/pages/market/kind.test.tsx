import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { instrumentKindKey } from '@/modules/instrument-kind/useInstrumentKind';
import { fakeApi, json } from '@/test/fakeApi';
import { ideaPage, scannerState } from '@/test/ideas';
import { marketList, marketQueries } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

function routes() {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/market/pairs': marketList(831),
    'GET /api/ideas/state': () => json(scannerState()),
    'GET /api/ideas': ideaPage([]),
    'GET /api/ideas/strategies': () => json({ total: 0, strategies: [], filterGone: false }),
  });
}

const last = (requests: readonly Request[]) => marketQueries(requests).at(-1)!;
const kindChoice = () => screen.getByRole('combobox', { name: 'Вид' });

// Change add-instrument-kind-filter, task 5.2: spec market-overview «Раздел «Рынок»», the choice of the kind.
describe('the kind of a pair in «Рынок»', () => {
  afterEach(() => vi.restoreAllMocks());

  // Scenario «Выбор вида».
  it('chooses the pairs of a kind from the first page', async () => {
    const requests = routes();
    const { history } = renderApp('/market?page=2');
    await screen.findByRole('row', { name: 'P781USDT' });
    expect(kindChoice()).toHaveValue('');
    expect(last(requests).has('kind')).toBe(false);
    expect(
      Array.from((kindChoice() as HTMLSelectElement).options).map((option) => option.text),
    ).toEqual(['Вид: все', 'крипта', 'акции']);

    await userEvent.selectOptions(kindChoice(), 'crypto');

    await waitFor(() => expect(last(requests).get('kind')).toBe('crypto'));
    expect(last(requests).get('page')).toBe('1');
    expect(history.location.search).not.toContain('page=');

    await userEvent.selectOptions(kindChoice(), '');
    await waitFor(() => expect(last(requests).has('kind')).toBe(false));
  });

  // Scenario «Вид помнится».
  it('keeps the kind in the browser over a reload', async () => {
    routes();
    const first = renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });
    await userEvent.selectOptions(kindChoice(), 'stock');
    first.unmount();

    const again = routes();
    renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });

    expect(kindChoice()).toHaveValue('stock');
    expect(marketQueries(again)[0]!.get('kind')).toBe('stock');
    expect(localStorage.getItem(instrumentKindKey)).toBe('stock');
  });

  // Scenario «Вид общий с „Идеями“».
  it('has the kind chosen in «Идеи»', async () => {
    const requests = routes();
    const { history } = renderApp('/ideas');
    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Вид' }), 'crypto');

    act(() => history.push('/market'));

    await screen.findByRole('row', { name: 'P831USDT' });
    expect(kindChoice()).toHaveValue('crypto');
    expect(marketQueries(requests).every((query) => query.get('kind') === 'crypto')).toBe(true);
  });

  // Scenario «Браузер не помнит».
  it('starts with every kind and still chooses when the browser keeps nothing', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    const requests = routes();
    renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });

    expect(kindChoice()).toHaveValue('');
    expect(last(requests).has('kind')).toBe(false);
    await userEvent.selectOptions(kindChoice(), 'stock');
    await waitFor(() => expect(last(requests).get('kind')).toBe('stock'));
  });
});
