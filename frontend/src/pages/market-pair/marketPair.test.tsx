import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createChart } from 'lightweight-charts';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import { marketRow } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const now = Date.parse('2026-11-04T12:00:00Z');
const day = 86_400_000;

const sol = marketRow({
  symbol: 'SOLUSDT',
  computedAt: new Date(now - 14_000).toISOString(),
  price: '168.42',
  change5m: '0.35',
  change15m: '-1.12',
  change1h: '3.48',
  change4h: '4.1',
  change24h: '12.4',
  spread: '0.0123',
  turnover1h: '142000000',
  turnover24h: '3500000000',
  openInterest: '2410000000',
  openInterestChange1h: '2.6',
  openInterestChange24h: '-5.3',
  fundingRate: '0.000118',
});
const solSpot = marketRow({ category: 'spot', symbol: 'SOLUSDT', price: '168.4', change24h: '-3' });

const candle = (openTime: string) => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close: '105',
  volume: '7',
  turnover: '700',
});

/** The query of a request by lower-case names: the API binds them without regard to case. */
const queryOf = (request: Request) =>
  new Map(
    [...new URL(request.url).searchParams].map(([name, value]) => [name.toLowerCase(), value]),
  );

function routes(category: 'linear' | 'spot', overrides: Record<string, FakeRoute> = {}) {
  const base = `/api/market/${category}/SOLUSDT`;

  return {
    [`GET ${base}`]: () => json(category === 'linear' ? sol : solSpot),
    [`GET ${base}/candles`]: () =>
      json({
        interval: '15',
        candles: [candle('2026-11-04T11:30:00Z'), candle('2026-11-04T11:45:00Z')],
        unavailable: false,
      }),
    [`GET ${base}/open-interest`]: () =>
      json([
        { time: '2026-11-04T11:00:00Z', value: '1200.5' },
        { time: '2026-11-04T11:05:00Z', value: '1190' },
      ]),
    [`GET ${base}/funding`]: () => json([{ time: '2026-11-04T08:00:00Z', rate: '0.0001' }]),
    ...overrides,
  };
}

/** The series of a given type the charts on the screen were given, with the data of their last `setData`. */
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

/** The value of a metric of the strip or of a card, found by its label. */
function metricOf(metrics: HTMLElement, label: string): HTMLElement {
  return within(metrics).getByText(label, { selector: 'dt' }).parentElement!.querySelector('dd')!;
}

describe('market pair page', () => {
  beforeEach(() => {
    vi.mocked(createChart).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(now);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Пара linear".
  it('shows the metrics of a linear pair, its 15 minute candles with volume and both side charts', async () => {
    const requests = fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    expect(await screen.findByRole('heading', { name: 'SOLUSDT' })).toBeInTheDocument();
    const header = screen.getByLabelText('Пара');
    expect(within(header).getByText('linear')).toHaveClass('text-run');
    expect(within(header).getByText('168.42')).toBeInTheDocument();
    expect(within(header).getByText('+12.40 %')).toHaveClass('text-ok');
    expect(within(header).getByText('за 24 ч')).toBeInTheDocument();
    expect(within(header).getByText('Обновлено 14 с назад')).toBeInTheDocument();
    expect(
      within(header).getByText(
        'Bybit · бессрочный USDT-контракт · свечи по 1 мин, OI каждые 5 мин',
      ),
    ).toBeInTheDocument();
    const metrics = await screen.findByRole('region', { name: 'Метрики' });
    expect(metricOf(metrics, 'Изм. 5м')).toHaveTextContent('+0.35 %');
    expect(metricOf(metrics, 'Изм. 15м')).toHaveTextContent('-1.12 %');
    expect(within(metricOf(metrics, 'Изм. 15м')).getByText('-1.12 %')).toHaveClass('text-err');
    expect(metricOf(metrics, 'Изм. 1ч')).toHaveTextContent('+3.48 %');
    expect(metricOf(metrics, 'Изм. 4ч')).toHaveTextContent('+4.10 %');
    expect(metricOf(metrics, 'Изм. 24ч')).toHaveTextContent('+12.40 %');
    expect(metricOf(metrics, 'Спред')).toHaveTextContent('0.01 %');
    expect(metricOf(metrics, 'Оборот 1ч, USDT')).toHaveTextContent('142 млн');
    expect(metricOf(metrics, 'Оборот 24ч, USDT')).toHaveTextContent('3.50 млрд');
    expect(metricOf(metrics, 'OI, USDT')).toHaveTextContent('2.41 млрд');
    expect(metricOf(metrics, 'ΔOI 1ч')).toHaveTextContent('+2.6 %');
    expect(metricOf(metrics, 'ΔOI 24ч')).toHaveTextContent('-5.3 %');
    expect(metricOf(metrics, 'Фандинг')).toHaveTextContent('+0.0118 %');
    expect(
      await screen.findByRole('img', { name: 'График свечей SOLUSDT, 15 мин' }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole('img', { name: 'Открытый интерес SOLUSDT' }),
    ).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: 'Фандинг SOLUSDT' })).toBeInTheDocument();
    await waitFor(() => expect(drawn('Candlestick')[0]).toHaveLength(2));
    expect(drawn('Histogram')).toHaveLength(2);
    expect(drawn('Line')[0]).toHaveLength(2);

    const candles = requests.find((request) => request.url.includes('/candles'))!;
    expect(queryOf(candles).get('interval')).toBe('15');
    // Open interest of the last 3 days (864 points of 5 minutes, within the 1000 the API gives) and funding of the last 10.
    for (const [part, days] of [
      ['/open-interest', 3],
      ['/funding', 10],
    ] as const) {
      const query = queryOf(requests.find((request) => request.url.includes(part))!);
      expect(Date.parse(query.get('from')!) - (now - days * day)).toBeLessThan(5000);
      expect(Date.parse(query.get('from')!)).toBeGreaterThanOrEqual(now - days * day);
    }
  });

  it('writes the open interest axis in compact USDT', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');
    await screen.findByRole('img', { name: 'Открытый интерес SOLUSDT' });

    const options = vi
      .mocked(createChart)
      .mock.calls.map(
        ([, chart]) =>
          chart?.localization?.priceFormatter as ((value: number) => string) | undefined,
      )
      .filter((format) => format !== undefined);
    expect(options.map((format) => format(2410000000))).toContain('2.41 млрд');
  });

  it.each([
    ['/open-interest', 'Открытый интерес'],
    ['/funding', 'Фандинг'],
  ])('says there is no data while a side chart has no points (%s)', async (part, region) => {
    fakeApi(routes('linear', { [`GET /api/market/linear/SOLUSDT${part}`]: () => json([]) }));
    renderApp('/market/linear/SOLUSDT');

    expect(
      await within(await screen.findByRole('region', { name: region })).findByText('Нет данных'),
    ).toBeInTheDocument();
  });

  it('tells the last closed candle above the chart', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    const legend = await screen.findByRole('group', { name: 'Свеча' });
    await waitFor(() => expect(legend).toHaveTextContent('04.11 14:45'));
    expect(legend).toHaveTextContent('O 100');
    expect(legend).toHaveTextContent('H 110');
    expect(legend).toHaveTextContent('L 95');
    expect(legend).toHaveTextContent('C 105');
    expect(legend).toHaveTextContent('Объём 7');
    expect(legend).not.toHaveTextContent('не закрыта');
  });

  it.each([
    ['/open-interest', 'Не удалось загрузить открытый интерес.'],
    ['/funding', 'Не удалось загрузить фандинг.'],
  ])(
    'says that a side chart could not be loaded instead of drawing it empty (%s)',
    async (part, message) => {
      fakeApi(
        routes('linear', {
          [`GET /api/market/linear/SOLUSDT${part}`]: () =>
            json(
              {
                title: 'One or more validation errors occurred.',
                status: 400,
                errors: { from: ['Too long.'] },
              },
              400,
            ),
        }),
      );
      renderApp('/market/linear/SOLUSDT');

      expect(await screen.findByText(message)).toBeInTheDocument();
    },
  );

  // Scenario "Пара spot".
  it('shows a spot pair with metrics and candles but without open interest and funding', async () => {
    const requests = fakeApi(routes('spot'));
    renderApp('/market/spot/SOLUSDT');

    expect(
      await screen.findByRole('img', { name: 'График свечей SOLUSDT, 15 мин' }),
    ).toBeInTheDocument();
    const metrics = screen.getByRole('region', { name: 'Метрики' });
    expect(within(metricOf(metrics, 'Изм. 24ч')).getByText('-3.00 %')).toHaveClass('text-err');
    // A spot pair has no open interest, no change of it and no funding.
    expect(within(metrics).queryByText('OI, USDT')).not.toBeInTheDocument();
    expect(within(metrics).queryByText('ΔOI 1ч')).not.toBeInTheDocument();
    expect(within(metrics).queryByText('Фандинг')).not.toBeInTheDocument();
    expect(screen.getByText('Bybit · спотовая USDT-пара · свечи по 1 мин')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Открытый интерес SOLUSDT' })).not.toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Фандинг SOLUSDT' })).not.toBeInTheDocument();
    expect(requests.some((request) => /open-interest|funding/.test(request.url))).toBe(false);
  });

  // Scenario "Неизвестная пара".
  it('says that there is no such pair on the market and leads back to the market', async () => {
    const requests = fakeApi({
      'GET /api/market/spot/NOPEUSDT': () => problem(404, 'no such pair'),
    });
    renderApp('/market/spot/NOPEUSDT');

    expect(await screen.findByText('Такой пары нет на рынке.')).toBeInTheDocument();
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Рынок' })).toHaveAttribute(
      'href',
      '/market',
    );
    expect(requests.some((request) => request.url.includes('/candles'))).toBe(false);
  });

  it('says that the pair could not be loaded when the answer is not a 404', async () => {
    fakeApi({ 'GET /api/market/spot/SOLUSDT': () => problem(400, 'bad') });
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить пару.');
  });

  it('shows the interval of the address and switches it through the address', async () => {
    const requests = fakeApi(routes('linear'));
    const { history } = renderApp('/market/linear/SOLUSDT?interval=60');
    await screen.findByRole('img', { name: 'График свечей SOLUSDT, 1 ч' });

    expect(screen.getByRole('button', { name: '1 ч' })).toHaveAttribute('aria-pressed', 'true');
    expect(
      queryOf(requests.find((request) => request.url.includes('/candles'))!).get('interval'),
    ).toBe('60');
    expect(
      within(screen.getByRole('group', { name: 'Интервал' }))
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['1 мин', '5 мин', '15 мин', '1 ч', '4 ч', '1 д']);

    await userEvent.click(screen.getByRole('button', { name: '4 ч' }));

    await waitFor(() => expect(history.location.search).toBe('?interval=240'));
    expect(
      await screen.findByRole('img', { name: 'График свечей SOLUSDT, 4 ч' }),
    ).toBeInTheDocument();
  });

  it('falls back to 15 minutes for an interval the switcher does not offer', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT?interval=3');

    expect(
      await screen.findByRole('img', { name: 'График свечей SOLUSDT, 15 мин' }),
    ).toBeInTheDocument();
  });

  it('reads the closed candles again once a minute', async () => {
    const requests = fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');
    await screen.findByRole('img', { name: 'График свечей SOLUSDT, 15 мин' });
    const before = requests.filter((request) => request.url.includes('/candles')).length;

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    await waitFor(() =>
      expect(requests.filter((request) => request.url.includes('/candles')).length).toBeGreaterThan(
        before,
      ),
    );
  });

  it('reads the row of the pair again every 15 seconds', async () => {
    let price = '168.42';
    fakeApi(routes('linear', { 'GET /api/market/linear/SOLUSDT': () => json({ ...sol, price }) }));
    renderApp('/market/linear/SOLUSDT');
    await screen.findByText('168.42');
    price = '170';

    await act(() => vi.advanceTimersByTimeAsync(16_000));

    expect(await screen.findByText('170')).toBeInTheDocument();
  });

  it('says that the candles are loading', async () => {
    fakeApi(
      routes('spot', {
        'GET /api/market/spot/SOLUSDT/candles': () => new Promise<Response>(() => undefined),
      }),
    );
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByText('Загрузка свечей…')).toBeInTheDocument();
  });

  it('says that there are no candles for the period', async () => {
    fakeApi(
      routes('spot', {
        'GET /api/market/spot/SOLUSDT/candles': () =>
          json({ interval: '15', candles: [], unavailable: false }),
      }),
    );
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByText('Свечей за период нет.')).toBeInTheDocument();
  });

  it('says that the history is unavailable when Bybit does not answer', async () => {
    fakeApi(
      routes('spot', {
        'GET /api/market/spot/SOLUSDT/candles': () =>
          json({ interval: '15', candles: [], unavailable: true }),
      }),
    );
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByText('История недоступна: Bybit не отвечает')).toBeInTheDocument();
  });

  it('says that the candles could not be loaded', async () => {
    fakeApi(
      routes('spot', {
        'GET /api/market/spot/SOLUSDT/candles': () => problem(400, 'bad'),
      }),
    );
    renderApp('/market/spot/SOLUSDT');

    expect(await screen.findByText('Не удалось загрузить свечи.')).toBeInTheDocument();
  });
});
