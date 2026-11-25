import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { instrumentKindKey } from '@/modules/instrument-kind/useInstrumentKind';
import { resetDrawn } from '@/test/chart';
import { FakeEventSource } from '@/test/eventSource';
import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea, ideaPage, scannerState } from '@/test/ideas';
import { pair } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const tsla = idea({ id: 'idea-tsla', symbol: 'TSLAUSDT', strategy: 'breakout-long' });
const tslaClosed = idea({ id: 'idea-tsla-closed', symbol: 'TSLAUSDT', state: 'closed' });

function pairRoutes(symbol: string, kind: 'crypto' | 'stock'): Record<string, FakeRoute> {
  return {
    [`GET /api/market/pairs/${symbol}`]: () => json(pair(symbol, { kind })),
    [`GET /api/market/pairs/${symbol}/chart`]: () =>
      json({ symbol, timeframe: '1h', loading: false, candles: [], funding: [] }),
    [`GET /api/market/pairs/${symbol}/chart/open-interest`]: () =>
      json({ symbol, timeframe: '1h', loading: false, openInterest: [] }),
    [`GET /api/market/pairs/${symbol}/ideas`]: () =>
      json({ items: [tsla, tslaClosed], total: 2, page: 1, pageSize: 50, filterGone: false }),
  };
}

function routes() {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/ideas/state': () => json(scannerState()),
    'GET /api/ideas': ideaPage([tsla]),
    'GET /api/ideas/strategies': () => json({ total: 1, strategies: [], filterGone: false }),
    ...pairRoutes('TSLAUSDT', 'stock'),
    ...pairRoutes('BTCUSDT', 'crypto'),
  });
}

beforeEach(() => {
  resetDrawn();
  FakeEventSource.reset();
  vi.stubGlobal('EventSource', FakeEventSource);
});

afterEach(() => vi.unstubAllGlobals());

// Change add-instrument-kind-filter, task 5.3: spec market-overview «Страница пары».
describe('the kind of a pair on its page', () => {
  // Scenario «Пометка вида».
  it('writes the kind of the pair beside its symbol', async () => {
    routes();
    renderApp('/market/TSLAUSDT');

    const mark = await screen.findByLabelText('Вид пары');
    expect(mark).toHaveTextContent('акция');
    expect(mark).toHaveClass('text-muted');
    const title = screen.getByRole('heading', { name: 'TSLAUSDT' });
    expect(title.parentElement).toContainElement(mark);
  });

  it('writes «крипта» beside a crypto pair', async () => {
    routes();
    renderApp('/market/BTCUSDT');

    expect(await screen.findByLabelText('Вид пары')).toHaveTextContent('крипта');
  });

  // Scenario «Выбор вида не действует».
  it('shows every idea of the pair whatever kind «Идеи» chose, with the idea chosen in its table', async () => {
    localStorage.setItem(instrumentKindKey, 'crypto');
    const requests = routes();
    renderApp('/ideas');
    expect(await screen.findByRole('combobox', { name: 'Вид' })).toHaveValue('crypto');

    await userEvent.click(await screen.findByRole('row', { name: 'TSLAUSDT breakout-long 1h' }));

    const table = within(await screen.findByRole('table', { name: 'Идеи пары' }));
    expect(await table.findByRole('row', { name: /idea-tsla$/ })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(table.getByRole('row', { name: /idea-tsla-closed$/ })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Вид' })).toBeNull();
    await waitFor(() =>
      expect(
        requests
          .map((request) => new URL(request.url))
          .filter((url) => url.pathname === '/api/market/pairs/TSLAUSDT/ideas').length,
      ).toBeGreaterThan(0),
    );
    expect(
      requests
        .map((request) => new URL(request.url))
        .filter((url) => url.pathname === '/api/market/pairs/TSLAUSDT/ideas')
        .every((url) => !url.searchParams.has('kind')),
    ).toBe(true);
  });
});
