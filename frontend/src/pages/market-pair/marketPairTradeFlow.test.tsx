import { act, screen, waitFor, within } from '@testing-library/react';
import { createChart } from 'lightweight-charts';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import { marketRow } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

/** The handlers the panel gave its channel, so a test speaks for the hub. */
interface Hub {
  onSnapshot: (snapshot: { state: string; flow: object | null }, superseded: boolean) => void;
  onLost: () => void;
}

let hub: Hub | undefined;

vi.mock('@/modules/trade-flow/realtime/followTradeFlow', () => ({
  followTradeFlow: (_category: string, _symbol: string, handlers: Hub) => {
    hub = handlers;

    return () => undefined;
  },
}));

const now = Date.parse('2026-11-04T12:00:00Z');

const candle = (openTime: string) => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close: '105',
  volume: '7',
  turnover: '700',
});

const bar = (openTime: string, buyTurnover: string, sellTurnover: string) => ({
  openTime,
  buyVolume: '1',
  sellVolume: '1',
  buyTurnover,
  sellTurnover,
  buyTrades: 2,
  sellTrades: 1,
});

const flow = (overrides: object = {}) => ({
  interval: '15',
  since: '2026-10-01T00:00:00Z',
  bars: [bar('2026-11-04T11:30:00Z', '100', '150.5'), bar('2026-11-04T11:45:00Z', '451', '447')],
  gaps: [],
  ...overrides,
});

const queryOf = (request: Request) =>
  new Map(
    [...new URL(request.url).searchParams].map(([name, value]) => [name.toLowerCase(), value]),
  );

function routes(category: 'linear' | 'spot', overrides: Record<string, FakeRoute> = {}) {
  const base = `/api/market/${category}/SOLUSDT`;

  return {
    [`GET ${base}`]: () => json(marketRow({ category, symbol: 'SOLUSDT' })),
    [`GET ${base}/candles`]: () =>
      json({
        interval: '15',
        candles: [candle('2026-11-04T11:30:00Z'), candle('2026-11-04T11:45:00Z')],
        unavailable: false,
      }),
    [`GET ${base}/open-interest`]: () => json([]),
    [`GET ${base}/funding`]: () => json([]),
    [`GET ${base}/trade-flow`]: () => json(flow()),
    ...overrides,
  };
}

/** The data of the last `setData` of every series of a type the charts on the screen were given. */
function drawn(type: string): unknown[][] {
  return vi.mocked(createChart).mock.results.flatMap((chart) => {
    const add = (chart.value as { addSeries: ReturnType<typeof vi.fn> }).addSeries;

    return add.mock.calls.flatMap(([kind], index) => {
      const series = add.mock.results[index]!.value as { setData: ReturnType<typeof vi.fn> };

      return kind === type && series.setData.mock.calls.length > 0
        ? [series.setData.mock.calls.at(-1)![0] as unknown[]]
        : [];
    });
  });
}

describe('market pair page, panel «Покупки и продажи»', () => {
  beforeEach(() => {
    vi.mocked(createChart).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(now);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Пара linear на 15 минутах".
  it('draws the buys up, the sells down and the CVD under the 15 minute candles of a linear pair', async () => {
    const requests = fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    expect(
      await within(panel).findByRole('img', { name: 'Покупки и продажи SOLUSDT, 15 мин' }),
    ).toBeInTheDocument();
    const caption = await within(panel).findByRole('group', { name: 'Последний бар' });
    expect(caption).toHaveTextContent('Покупки 451');
    expect(caption).toHaveTextContent('Продажи 447');
    expect(caption).toHaveTextContent('Разность +4');
    expect(within(panel).getByText('подключение')).toBeInTheDocument();
    await waitFor(() =>
      expect(drawn('Histogram')).toContainEqual([
        expect.objectContaining({ value: 100 }),
        expect.objectContaining({ value: 451 }),
      ]),
    );
    expect(drawn('Histogram')).toContainEqual([
      expect.objectContaining({ value: -150.5 }),
      expect.objectContaining({ value: -447 }),
    ]);
    expect(drawn('Line')).toContainEqual([
      expect.objectContaining({ value: -50.5 }),
      expect.objectContaining({ value: -46.5 }),
    ]);
    const query = queryOf(requests.find((request) => request.url.includes('/trade-flow'))!);
    expect(query.get('interval')).toBe('15');
    expect(Date.parse(query.get('to')!)).toBeGreaterThanOrEqual(now);
  });

  // Scenario "Пара spot".
  it('shows the panel for a spot pair too', async () => {
    fakeApi(routes('spot'));
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByRole('region', { name: 'Покупки и продажи' })).toBeInTheDocument();
  });

  // Scenario "Пара до начала сбора": the first minute of the pair in the local time of the browser.
  it('tells that the pair has no data before its first minute', async () => {
    fakeApi(
      routes('linear', {
        'GET /api/market/linear/SOLUSDT/trade-flow': () =>
          json(flow({ since: '2026-11-04T06:00:00Z' })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    expect(await within(panel).findByText('данных нет до 04.11 09:00')).toBeInTheDocument();
  });

  it('tells that the pair has no data yet', async () => {
    fakeApi(
      routes('linear', {
        'GET /api/market/linear/SOLUSDT/trade-flow': () => json(flow({ since: null, bars: [] })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    expect(await within(panel).findByText('данных пока нет')).toBeInTheDocument();
    expect(within(panel).queryByRole('group', { name: 'Последний бар' })).toBeNull();
  });

  // Scenario "Разрыв на панели".
  it('marks the gaps of the flow', async () => {
    fakeApi(
      routes('linear', {
        'GET /api/market/linear/SOLUSDT/trade-flow': () =>
          json(flow({ gaps: [{ from: '2026-11-04T11:02:00Z', to: '2026-11-04T11:05:00Z' }] })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    expect(await within(panel).findByText('Разрыв 04.11 14:02–14:05')).toBeInTheDocument();
  });

  // Scenario "API баров недоступен": the panel says so, the candles of the page stay.
  it('says the trade flow is unavailable and leaves the rest of the page', async () => {
    fakeApi(
      routes('linear', { 'GET /api/market/linear/SOLUSDT/trade-flow': () => problem(500, 'down') }),
    );
    renderApp('/market/linear/SOLUSDT');

    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    // A server error is tried again once before the panel gives up.
    expect(await within(panel).findByRole('alert', {}, { timeout: 5000 })).toHaveTextContent(
      'Данные потока сделок недоступны.',
    );
    expect(
      await screen.findByRole('img', { name: 'График свечей SOLUSDT, 15 мин' }),
    ).toBeInTheDocument();
    await waitFor(() => expect(drawn('Candlestick')[0]).toHaveLength(2));
  });

  // Scenarios "Страница открыта посреди минуты" and "Связь потеряна": the live minute joins the current bar, marked incomplete; a lost channel
  // has no data and leaves the closed bars.
  it('adds the live minute to the current bar, marks it incomplete and keeps the bars when the channel is lost', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');
    const panel = await screen.findByRole('region', { name: 'Покупки и продажи' });
    await within(panel).findByRole('group', { name: 'Последний бар' });

    act(() =>
      hub!.onSnapshot(
        {
          state: 'live',
          flow: {
            ...bar('2026-11-04T12:00:00Z', '49', '3'),
            since: '2026-11-04T12:00:00.500Z',
          },
        },
        false,
      ),
    );

    const caption = within(panel).getByRole('group', { name: 'Последний бар' });
    expect(caption).toHaveTextContent('Покупки 49');
    expect(caption).toHaveTextContent('неполный');
    expect(within(panel).getByText('в реальном времени')).toBeInTheDocument();
    // The current bar is drawn apart from the closed ones.
    const current = Date.parse('2026-11-04T12:00:00Z') / 1000;
    await waitFor(() =>
      expect(drawn('Histogram')).toContainEqual(
        expect.arrayContaining([
          expect.objectContaining({ time: current, value: 49, color: expect.any(String) }),
        ]),
      ),
    );
    expect(drawn('Histogram').flat()).toContainEqual(
      expect.not.objectContaining({ color: expect.anything() }),
    );
    act(() => hub!.onLost());
    expect(within(panel).getByText('нет данных')).toBeInTheDocument();
    expect(within(panel).getByRole('group', { name: 'Последний бар' })).toHaveTextContent(
      'Покупки 49',
    );
  });

  it('has no panel for a share of moex', async () => {
    fakeApi({
      'GET /api/market/moex/SBER': () => json(marketRow({ category: 'moex', symbol: 'SBER' })),
      'GET /api/market/moex/SBER/candles': () =>
        json({ interval: '15', candles: [], unavailable: false }),
    });
    renderApp('/market/moex/SBER');

    expect(await screen.findByRole('heading', { name: 'SBER' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Покупки и продажи' })).toBeNull();
  });
});
