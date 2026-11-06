import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createChart, createSeriesMarkers } from 'lightweight-charts';

import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { ideaOf, marketRow } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const sol = marketRow({ symbol: 'SOLUSDT', price: '168.42' });
const solSpot = marketRow({ category: 'spot', symbol: 'SOLUSDT', price: '168.4' });

const pullback = ideaOf({
  id: 'idea-pullback',
  symbol: 'SOLUSDT',
  type: 'pullback',
  interval: '60',
  firedAt: '2026-11-04T10:47:00Z',
  entry: '168.40',
  stop: '165.00',
  target: '175.00',
  riskReward: '1.94',
});
const levels = ideaOf({
  id: 'idea-levels',
  symbol: 'SOLUSDT',
  type: 'levels',
  side: 'short',
  interval: '5',
  status: 'active',
  entry: '170',
  stop: '171',
  target: '167',
});
const expired = ideaOf({
  id: 'idea-expired',
  symbol: 'SOLUSDT',
  type: 'beyond_extreme',
  status: 'expired',
});

const candle = (openTime: string) => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close: '105',
  volume: '7',
  turnover: '700',
});

function routes(category: 'linear' | 'spot', overrides: Record<string, FakeRoute> = {}) {
  const base = `/api/market/${category}/SOLUSDT`;

  return {
    [`GET ${base}`]: () => json(category === 'linear' ? sol : solSpot),
    [`GET ${base}/candles`]: () =>
      json({ interval: '60', candles: [candle('2026-11-04T10:00:00Z')], unavailable: false }),
    [`GET ${base}/open-interest`]: () => json([]),
    [`GET ${base}/funding`]: () => json([]),
    [`GET ${base}/ideas`]: () => json([pullback, levels, expired]),
    [`GET ${base}/zones`]: () =>
      json({
        price: '168.4',
        zones: [
          { low: '160', high: '162', strength: 3, kind: 'support' },
          { low: '175', high: '176', strength: 2, kind: 'resistance' },
        ],
      }),
    [`POST ${base}/own-idea`]: () => json({ made: false }),
    ...overrides,
  };
}

type Series = {
  createPriceLine: ReturnType<typeof vi.fn>;
  removePriceLine: ReturnType<typeof vi.fn>;
  attachPrimitive: ReturnType<typeof vi.fn>;
  priceToCoordinate: ReturnType<typeof vi.fn>;
};

/** The candle series of the chart of the pair, the only one the page draws with candles. */
function candleSeries(): Series {
  const found = vi.mocked(createChart).mock.results.flatMap((chart) => {
    const add = (chart.value as { addSeries: ReturnType<typeof vi.fn> }).addSeries;

    return add.mock.calls.flatMap(([kind], index) =>
      kind === 'Candlestick' ? [add.mock.results[index]!.value as Series] : [],
    );
  });

  return found.at(-1)!;
}

/** The price lines on the chart now: the ones created minus the ones removed. */
function linesOnChart() {
  const series = candleSeries();
  const created = series.createPriceLine.mock.calls.map(([line]) => line as { title: string });
  const removed = series.removePriceLine.mock.calls.length;

  return created.slice(removed).map((line) => line.title);
}

/** Draws the zones the chart gave its primitive on a fake canvas and returns the colours of the bands. */
function zoneColours() {
  const series = candleSeries();
  series.priceToCoordinate.mockImplementation((price: number) => 1000 - price);
  const primitive = series.attachPrimitive.mock.calls[0]![0] as {
    attached: (param: unknown) => void;
    paneViews: () => { renderer: () => { draw: (target: unknown) => void } }[];
  };
  primitive.attached({ series, requestUpdate: () => undefined });
  const fills: string[] = [];
  const context = {
    fillStyle: '',
    fillRect: () => fills.push(context.fillStyle),
  };
  primitive
    .paneViews()[0]!
    .renderer()
    .draw({
      useBitmapCoordinateSpace: (draw: (scope: unknown) => void) =>
        draw({ context, bitmapSize: { width: 800, height: 400 }, verticalPixelRatio: 1 }),
    });

  return fills;
}

const lastMarkers = () =>
  (
    vi.mocked(createSeriesMarkers).mock.results.at(-1)!.value as {
      setMarkers: ReturnType<typeof vi.fn>;
    }
  ).setMarkers.mock.calls.at(-1)![0] as { text: string; time: number }[];

const requested = (requests: Request[], pathname: string) =>
  requests.filter((request) => new URL(request.url).pathname === pathname);

/** The rows of the table of the ideas of the pair under its header, by their text. */
const rowTexts = () =>
  within(screen.getByRole('region', { name: 'Идеи пары' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.textContent);

/** The row of the table of the pair that has the text; its found time is the button that selects it. */
async function rowOf(text: string) {
  const table = await within(await screen.findByRole('region', { name: 'Идеи пары' })).findByRole(
    'table',
  );

  return within(table)
    .getAllByRole('row')
    .find((row) => row.textContent.includes(text))!;
}

const pressed = async (text: string) =>
  within(await rowOf(text))
    .getByRole('button')
    .getAttribute('aria-pressed');

describe('ideas on the pair page', () => {
  beforeEach(() => {
    vi.mocked(createChart).mockClear();
    vi.mocked(createSeriesMarkers).mockClear();
  });

  afterEach(() => vi.unstubAllGlobals());

  // Scenario "Раскладка страницы": the chart across the page, the ideas under it, the own idea below them.
  it('puts the chart on top, the table of the ideas under it and the own idea below', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    const ideas = await screen.findByRole('region', { name: 'Идеи пары' });
    const chart = screen.getByRole('region', { name: 'График' });
    const own = screen.getByRole('region', { name: 'Своя идея' });

    expect(chart.compareDocumentPosition(ideas) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(ideas.compareDocumentPosition(own) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // A narrow screen scrolls the table inside its section.
    expect((await within(ideas).findByRole('table')).parentElement).toHaveClass('overflow-x-auto');
  });

  // Scenario "Истёкшие свёрнуты".
  it('lists the open ideas first and keeps the expired ones under a closed «Истёкшие»', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    await rowOf('Откат');

    expect(rowTexts()).toEqual([
      expect.stringContaining('Откат'),
      expect.stringContaining('Уровни'),
      'Истёкшие · 1',
    ]);
    expect(screen.getByRole('button', { name: 'Истёкшие · 1' })).toHaveAttribute(
      'aria-expanded',
      'false',
    );
  });

  describe('money of an idea', () => {
    afterEach(() => localStorage.clear());

    // Scenario "Деньги в строке".
    it('shows the profit and the loss in USDT of the amount per idea', async () => {
      fakeApi(
        routes('linear', {
          'GET /api/market/linear/SOLUSDT/ideas': () => json([ideaOf({ symbol: 'SOLUSDT' })]),
        }),
      );
      renderApp('/market/linear/SOLUSDT');

      const row = within(await rowOf('Откат'));

      expect(row.getByText('+0.39')).toBeInTheDocument();
      expect(row.getByText('−0.21')).toBeInTheDocument();
    });

    it('takes the amount the owner set on the screen of the ideas', async () => {
      localStorage.setItem('profit-fox.ideas.amount', '30');
      fakeApi(
        routes('linear', {
          'GET /api/market/linear/SOLUSDT/ideas': () => json([ideaOf({ symbol: 'SOLUSDT' })]),
        }),
      );
      renderApp('/market/linear/SOLUSDT');

      const row = within(await rowOf('Откат'));

      expect(row.getByText('+1.17')).toBeInTheDocument();
      expect(row.getByText('−0.63')).toBeInTheDocument();
    });

    it('shows no money while the amount is not a positive number', async () => {
      localStorage.setItem('profit-fox.ideas.amount', '0');
      fakeApi(routes('linear'));
      renderApp('/market/linear/SOLUSDT');

      const row = within(await rowOf('Откат'));

      expect(row.queryByText('+0.00')).not.toBeInTheDocument();
      expect(row.getByText('—')).toBeInTheDocument();
    });
  });

  // Scenario "Вход по рынку и лимитный вход".
  it('shows the prices of the ideas of a pair by its price step', async () => {
    fakeApi(
      routes('linear', {
        'GET /api/market/linear/SOLUSDT/ideas': () =>
          json([
            ideaOf({
              id: 'market',
              symbol: 'SOLUSDT',
              entry: '184.7',
              stop: '180',
              target: '197.00',
              tick: '0.01',
            }),
          ]),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const row = within(await rowOf('Откат'));

    expect(row.getByText('184.70')).toBeInTheDocument();
    expect(row.getByText('180.00')).toBeInTheDocument();
    expect(row.getByText('197.00')).toBeInTheDocument();
    expect(row.getByText('1.84')).toHaveClass('text-ok');
    expect(row.getByText('без ком. 2.00')).toBeInTheDocument();
  });

  // Scenario "Пара без идей".
  it('explains an empty list and still offers an own idea', async () => {
    fakeApi(routes('linear', { 'GET /api/market/linear/SOLUSDT/ideas': () => json([]) }));
    renderApp('/market/linear/SOLUSDT');

    expect(await screen.findByText(/У пары нет идей/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Своя идея' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('draws no levels, zones or signal while no idea is selected', async () => {
    const requests = fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT');

    await rowOf('Откат');

    expect(linesOnChart()).toEqual([]);
    expect(lastMarkers()).toEqual([]);
    expect(requested(requests, '/api/market/linear/SOLUSDT/zones')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Снять выбор' })).not.toBeInTheDocument();
  });

  // Scenario "Переход из списка идей".
  it('shows the selected idea on the chart: its interval, levels, zones and the signal mark', async () => {
    const requests = fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT?idea=idea-pullback');

    expect(await pressed('Откат')).toBe('true');
    expect(await pressed('Уровни')).toBe('false');
    expect(
      within(screen.getByRole('group', { name: 'Интервал' })).getByRole('button', { name: '1 ч' }),
    ).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() =>
      expect(linesOnChart()).toEqual(['Вход 168.40', 'Стоп 165.00', 'Цель 175.00']),
    );
    await waitFor(() => expect(zoneColours()).toHaveLength(2));
    expect(zoneColours()).toEqual(['rgba(63, 179, 127, 0.16)', 'rgba(229, 72, 77, 0.16)']);
    expect(
      requested(requests, '/api/market/linear/SOLUSDT/zones').map((request) =>
        new URL(request.url).searchParams.get('interval'),
      ),
    ).toEqual(['60']);
    // 10:47 on 1 hour is in the candle of 10:00.
    await waitFor(() =>
      expect(lastMarkers()).toEqual([
        expect.objectContaining({
          text: 'Сигнал',
          time: Date.parse('2026-11-04T10:00:00Z') / 1000,
        }),
      ]),
    );
  });

  // Scenario "Стоп-вход ждёт входа": the levels of the stop order on the chart of its interval, the status in its row.
  it('shows a stop entry of a squeeze awaiting its entry on the chart and in the table', async () => {
    const stopEntry = ideaOf({
      id: 'idea-stop-entry',
      symbol: 'SOLUSDT',
      setup: 'squeeze',
      type: 'stop_entry',
      interval: '240',
      status: 'awaiting_entry',
      entry: '170.01',
      stop: '164.50',
      target: '181.03',
    });
    fakeApi(routes('linear', { 'GET /api/market/linear/SOLUSDT/ideas': () => json([stopEntry]) }));
    renderApp('/market/linear/SOLUSDT?idea=idea-stop-entry');

    expect(await pressed('Стоп-вход')).toBe('true');
    expect((await rowOf('Стоп-вход')).textContent).toContain('Ждёт входа');
    expect((await rowOf('Стоп-вход')).textContent).toContain('Сжатие');
    expect(
      within(screen.getByRole('group', { name: 'Интервал' })).getByRole('button', { name: '4 ч' }),
    ).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() =>
      expect(linesOnChart()).toEqual(['Вход 170.01', 'Стоп 164.50', 'Цель 181.03']),
    );
  });

  // Scenario "Выбор строки в таблице пары".
  it('selects an idea of the table by putting it into the address', async () => {
    fakeApi(routes('linear'));
    const { history } = renderApp('/market/linear/SOLUSDT');

    await userEvent.click(await rowOf('Уровни'));

    await waitFor(() => expect(history.location.search).toBe('?idea=idea-levels'));
    await waitFor(async () => expect(await pressed('Уровни')).toBe('true'));
    await waitFor(() =>
      expect(
        within(screen.getByRole('group', { name: 'Интервал' })).getByRole('button', {
          name: '5 мин',
        }),
      ).toHaveAttribute('aria-pressed', 'true'),
    );
  });

  it('removes the idea from the address and the chart with «Снять выбор»', async () => {
    fakeApi(routes('linear'));
    const { history } = renderApp('/market/linear/SOLUSDT?idea=idea-pullback');
    await waitFor(() => expect(linesOnChart()).toHaveLength(3));

    await userEvent.click(screen.getByRole('button', { name: 'Снять выбор' }));

    await waitFor(() => expect(history.location.search).toBe(''));
    await waitFor(() => expect(linesOnChart()).toEqual([]));
    expect(lastMarkers()).toEqual([]);
    expect(await pressed('Откат')).toBe('false');
  });

  // Scenario "Выбрана истёкшая идея": the group stays closed, the chart shows the idea.
  it('keeps «Истёкшие» closed when an expired idea is selected and draws its levels', async () => {
    fakeApi(routes('linear'));
    renderApp('/market/linear/SOLUSDT?idea=idea-expired');

    await waitFor(() => expect(linesOnChart()).toEqual(['Вход 2.40', 'Стоп 2.30', 'Цель 2.60']));
    expect(screen.getByRole('button', { name: 'Истёкшие · 1' })).toHaveAttribute(
      'aria-expanded',
      'false',
    );
    expect(screen.queryByText('Истекла')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Снять выбор' })).toBeInTheDocument();
  });

  // Scenario "Выбор идеи внизу списка": the router scrolls to the top on a navigation unless told not to.
  describe('scroll position', () => {
    const scrollTo = vi.spyOn(window, 'scrollTo');

    beforeEach(() => scrollTo.mockClear());

    it('stays where it is when an idea is selected', async () => {
      fakeApi(routes('linear'));
      const { history } = renderApp('/market/linear/SOLUSDT');
      const idea = await rowOf('Уровни');
      scrollTo.mockClear();

      await userEvent.click(idea);

      await waitFor(() => expect(history.location.search).toBe('?idea=idea-levels'));
      expect(scrollTo).not.toHaveBeenCalled();
    });

    it('stays where it is when the selection is removed', async () => {
      fakeApi(routes('linear'));
      const { history } = renderApp('/market/linear/SOLUSDT?idea=idea-pullback');
      const remove = await screen.findByRole('button', { name: 'Снять выбор' });
      scrollTo.mockClear();

      await userEvent.click(remove);

      await waitFor(() => expect(history.location.search).toBe(''));
      expect(scrollTo).not.toHaveBeenCalled();
    });

    it('stays where it is when the interval of the chart is changed', async () => {
      fakeApi(routes('linear'));
      const { history } = renderApp('/market/linear/SOLUSDT');
      const group = await screen.findByRole('group', { name: 'Интервал' });
      scrollTo.mockClear();

      await userEvent.click(within(group).getByRole('button', { name: '15 мин' }));

      await waitFor(() => expect(history.location.search).toBe('?interval=15'));
      expect(scrollTo).not.toHaveBeenCalled();
    });
  });

  it('keeps the interval of the chart when the selection is removed', async () => {
    fakeApi(routes('linear'));
    const { history } = renderApp('/market/linear/SOLUSDT?idea=idea-pullback&interval=15');
    await screen.findByRole('button', { name: 'Снять выбор' });

    await userEvent.click(screen.getByRole('button', { name: 'Снять выбор' }));

    await waitFor(() => expect(history.location.search).toBe('?interval=15'));
  });

  // An idea of the address may have left the list of the pair: it is read by its id and shown, never the own idea instead.
  it('shows an idea of the address that is not in the list of the pair', async () => {
    const old = ideaOf({
      id: 'idea-old',
      symbol: 'SOLUSDT',
      interval: '60',
      entry: '150',
      stop: '140',
      target: '170',
    });
    fakeApi(
      routes('linear', {
        'GET /api/ideas/idea-old': () => json(old),
        'POST /api/market/linear/SOLUSDT/own-idea': () =>
          json({
            made: true,
            entry: '168.42',
            stop: '165',
            target: '175',
            riskReward: '1.9',
            netRiskReward: '1.56',
            breakEven: '0.3517',
            tick: null,
          }),
      }),
    );
    renderApp('/market/linear/SOLUSDT?idea=idea-old');

    await waitFor(() => expect(linesOnChart()).toEqual(['Вход 150', 'Стоп 140', 'Цель 170']));
    expect(screen.getByText('Идея больше не в списке пары.')).toBeInTheDocument();
  });

  it('draws nothing for an idea of the address that belongs to another pair', async () => {
    const other = ideaOf({
      id: 'idea-btc',
      symbol: 'BTCUSDT',
      interval: '60',
      entry: '64000',
      stop: '63000',
      target: '66000',
    });
    fakeApi(routes('linear', { 'GET /api/ideas/idea-btc': () => json(other) }));
    renderApp('/market/linear/SOLUSDT?idea=idea-btc');

    expect(await screen.findByText('Идеи больше нет.')).toBeInTheDocument();
    expect(linesOnChart()).toEqual([]);
  });

  it('draws nothing for an idea of the address that is gone', async () => {
    fakeApi(
      routes('linear', {
        'POST /api/market/linear/SOLUSDT/own-idea': () =>
          json({
            made: true,
            entry: '168.42',
            stop: '165',
            target: '175',
            riskReward: '1.9',
            netRiskReward: '1.56',
            breakEven: '0.3517',
            tick: null,
          }),
      }),
    );
    renderApp('/market/linear/SOLUSDT?idea=idea-gone');

    expect(await screen.findByText('Идеи больше нет.')).toBeInTheDocument();
    expect(linesOnChart()).toEqual([]);
  });

  it('keeps the idea in the address when the interval of the chart is changed', async () => {
    fakeApi(routes('linear'));
    const { history } = renderApp('/market/linear/SOLUSDT?idea=idea-pullback');
    await screen.findByRole('group', { name: 'Интервал' });

    await userEvent.click(
      within(screen.getByRole('group', { name: 'Интервал' })).getByRole('button', {
        name: '15 мин',
      }),
    );

    await waitFor(() => expect(history.location.search).toContain('idea=idea-pullback'));
    expect(history.location.search).toContain('interval=15');
    // The signal is of the 1 hour candle: it is not drawn on 15 minutes.
    await waitFor(() => expect(lastMarkers()).toEqual([]));
  });

  describe('own idea', () => {
    const blockOf = () => within(screen.getByRole('region', { name: 'Своя идея' }));
    const ready = () => screen.findByRole('region', { name: 'Своя идея' });

    it('shows the levels of an own idea, its break-even and the note, and draws them on the chart', async () => {
      const requests = fakeApi(
        routes('linear', {
          'POST /api/market/linear/SOLUSDT/own-idea': () =>
            json({
              made: true,
              entry: '168.42',
              stop: '165',
              target: '175',
              riskReward: '1.9',
              netRiskReward: '1.56',
              breakEven: '0.3517',
              tick: null,
            }),
        }),
      );
      renderApp('/market/linear/SOLUSDT');
      await ready();

      expect(await blockOf().findByText('168.42')).toBeInTheDocument();
      // Scenario "Своя идея": the R:R after the fees in the chip, the price ratio under it.
      expect(blockOf().getByText('1.56')).toHaveClass('text-ok');
      expect(blockOf().getByText('без ком. 1.90')).toBeInTheDocument();
      // Scenario "Безубыток своей идеи": rounded up.
      expect(blockOf().getByText('нужно ≥ 36 % целей')).toBeInTheDocument();
      expect(
        blockOf().getByText('без сетапа, при условии, что идея не истекает'),
      ).toBeInTheDocument();
      await waitFor(() => expect(linesOnChart()).toEqual(['Вход 168.42', 'Стоп 165', 'Цель 175']));
      expect(await requested(requests, '/api/market/linear/SOLUSDT/own-idea')[0]!.json()).toEqual({
        type: 'levels',
        interval: '15',
        side: 'long',
      });
    });

    // Scenario "Типы своей идеи": without a setup a stop entry has no range.
    it('offers the types at the market and the pullback, without the stop entry', async () => {
      fakeApi(routes('linear'));
      renderApp('/market/linear/SOLUSDT');
      await ready();

      expect(
        within(blockOf().getByRole('combobox', { name: 'Тип' }))
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Уровни', 'Цель 2R', 'За экстремумом', 'Откат']);
    });

    // Scenario "Цель не окупает комиссии".
    it('says the target of an own idea does not cover the fees', async () => {
      fakeApi(
        routes('linear', {
          'POST /api/market/linear/SOLUSDT/own-idea': () =>
            json({
              made: true,
              entry: '168.42',
              stop: '168.3',
              target: '168.6',
              riskReward: '1.5',
              netRiskReward: '-0.2',
              breakEven: null,
              tick: null,
            }),
        }),
      );
      renderApp('/market/linear/SOLUSDT');
      await ready();

      expect(await blockOf().findByText('не окупает комиссии')).toBeInTheDocument();
      expect(blockOf().queryByText(/при условии/)).not.toBeInTheDocument();
    });

    it('shows the levels of an own idea by the price step of the pair', async () => {
      fakeApi(
        routes('linear', {
          'POST /api/market/linear/SOLUSDT/own-idea': () =>
            json({
              made: true,
              entry: '168.4',
              stop: '165',
              target: '175.5',
              riskReward: '1.9',
              netRiskReward: '1.56',
              breakEven: '0.3517',
              tick: '0.01',
            }),
        }),
      );
      renderApp('/market/linear/SOLUSDT');
      await ready();

      expect(await blockOf().findByText('168.40')).toBeInTheDocument();
      expect(blockOf().getByText('165.00')).toBeInTheDocument();
      expect(blockOf().getByText('175.50')).toBeInTheDocument();
    });

    it('asks again for the type, interval and side the owner picks', async () => {
      const requests = fakeApi(routes('linear'));
      renderApp('/market/linear/SOLUSDT');
      await ready();
      await blockOf().findByText(/Идеи нет/);

      await userEvent.selectOptions(blockOf().getByRole('combobox', { name: 'Тип' }), 'pullback');
      await userEvent.selectOptions(blockOf().getByRole('combobox', { name: 'ТФ' }), '240');
      await userEvent.click(blockOf().getByRole('button', { name: 'Шорт' }));

      await waitFor(async () =>
        expect(
          await requested(requests, '/api/market/linear/SOLUSDT/own-idea').at(-1)!.json(),
        ).toEqual({
          type: 'pullback',
          interval: '240',
          side: 'short',
        }),
      );
    });

    it('explains that the rules make no idea', async () => {
      fakeApi(routes('linear'));
      renderApp('/market/linear/SOLUSDT');
      await ready();

      expect(await blockOf().findByText(/Идеи нет/)).toBeInTheDocument();
      expect(linesOnChart()).toEqual([]);
    });

    // Scenario "Своя идея на spot".
    it('does not offer short on spot and says why', async () => {
      const requests = fakeApi(routes('spot'));
      renderApp('/market/spot/SOLUSDT');
      await ready();

      expect(await blockOf().findByRole('button', { name: 'Шорт' })).toBeDisabled();
      expect(blockOf().getByRole('button', { name: 'Лонг' })).toBeEnabled();
      expect(blockOf().getByText('на spot шорта нет')).toBeInTheDocument();
      await userEvent.click(blockOf().getByRole('button', { name: 'Шорт' }));
      await waitFor(async () =>
        expect(
          await Promise.all(
            requested(requests, '/api/market/spot/SOLUSDT/own-idea').map((request) =>
              request.json(),
            ),
          ),
        ).toEqual([{ type: 'levels', interval: '15', side: 'long' }]),
      );
    });
  });
});
