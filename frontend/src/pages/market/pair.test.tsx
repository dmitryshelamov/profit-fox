import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Idea } from '@/api/ideas';
import type { ChartCandle } from '@/api/market';
import { drawn, resetDrawn } from '@/test/chart';
import { FakeEventSource } from '@/test/eventSource';
import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea } from '@/test/ideas';
import { pair } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const hour = 3_600_000;
const fourteen = Date.UTC(2026, 10, 22, 14);

/** `count` closed candles of the timeframe ending before `before`: the last one opened at `before - step`. */
function candlesBefore(before: number, step: number, count: number, close = 236): ChartCandle[] {
  return Array.from({ length: count }, (_, index) => ({
    openTime: before - (count - index) * step,
    open: close - 1,
    high: close + 1,
    low: close - 2,
    close,
    volume: 10,
  }));
}

function chartRoute(step = hour): FakeRoute {
  return (request) => {
    const query = new URL(request.url).searchParams;
    const before = Number(query.get('before') ?? fourteen);
    const candles = candlesBefore(before, step, 500);
    return json({
      symbol: 'SOLUSDT',
      timeframe: query.get('timeframe'),
      loading: false,
      candles,
      openInterest: candles.map((candle) => ({ time: candle.openTime, value: 24_000 })),
      funding: [
        { time: before - 16 * hour, value: 0.0001 },
        { time: before - 8 * hour, value: -0.00004 },
      ],
    });
  };
}

function routes(ideas: readonly Idea[] = [], extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/market/pairs/SOLUSDT': () =>
      json(
        pair('SOLUSDT', {
          price: 238.42,
          change24h: 0.0437,
          fundingRate: 0.000184,
          nextFunding: null,
        }),
      ),
    'GET /api/market/pairs/SOLUSDT/chart': chartRoute(),
    'GET /api/market/pairs/SOLUSDT/ideas': () =>
      json({ items: ideas, total: ideas.length, page: 1, pageSize: 50, filterGone: false }),
    ...extra,
  });
}

const chartQueries = (requests: readonly Request[]) =>
  requests
    .map((request) => new URL(request.url))
    .filter((url) => url.pathname === '/api/market/pairs/SOLUSDT/chart')
    .map((url) => url.searchParams);

beforeEach(() => {
  resetDrawn();
  FakeEventSource.reset();
  vi.stubGlobal('EventSource', FakeEventSource);
});

afterEach(() => vi.unstubAllGlobals());

// Spec market-overview «Страница пары» (task 14.2).
describe('the page of a pair', () => {
  // Scenario «До выплаты».
  it('counts the time to the next payment', async () => {
    routes([], {
      'GET /api/market/pairs/SOLUSDT': () =>
        json(pair('SOLUSDT', { nextFunding: Date.now() + (hour + 27 * 60_000 + 55_000) + 600 })),
    });
    renderApp('/market/SOLUSDT');

    await waitFor(() =>
      expect(screen.getByLabelText('До выплаты')).toHaveTextContent(/^01:27:5[45]$/),
    );
    expect(screen.getByLabelText('Цена')).toHaveTextContent('100.00+1.00 % за 24 ч');
  });

  // Scenario «Одна выплата».
  it('puts a dash for the time to the payment when only one payment is known', async () => {
    routes();
    renderApp('/market/SOLUSDT');

    expect(await screen.findByLabelText('До выплаты')).toHaveTextContent('—');
    expect(screen.getByLabelText('Цена')).toHaveTextContent('238.42+4.37 % за 24 ч');
    expect(screen.getByLabelText('Фандинг')).toHaveTextContent('0.0184 %');
  });

  // Scenario «Пары нет».
  it('says the pair does not exist and leads to «Рынок»', async () => {
    routes([], { 'GET /api/market/pairs/NOSUCHUSDT': () => json({ status: 404 }, 404) });
    renderApp('/market/NOSUCHUSDT');

    expect(await screen.findByRole('heading', { name: 'Пары NOSUCHUSDT нет' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← Рынок' })).toHaveAttribute('href', '/market');
    expect(screen.queryByTestId('chart')).toBeNull();
  });
});

// Spec market-overview «График пары» (task 14.2).
describe('the chart of a pair', () => {
  // Scenario «Смена таймфрейма».
  it('shows the candles of the timeframe chosen and puts it in the address', async () => {
    const requests = routes([], { 'GET /api/market/pairs/SOLUSDT/chart': chartRoute(15 * 60_000) });
    const { history } = renderApp('/market/SOLUSDT');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));

    await userEvent.click(screen.getByRole('radio', { name: '15m' }));

    await waitFor(() => expect(chartQueries(requests).at(-1)!.get('timeframe')).toBe('15m'));
    expect(history.location.search).toContain('timeframe=15m');
    expect(FakeEventSource.last().url).toBe('/api/market/pairs/SOLUSDT/live?timeframe=15m');
  });

  // Scenarios «Прокрутка назад» and «Текущие сутки».
  it('loads the earlier candles at the left edge and keeps the part in view', async () => {
    const requests = routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));
    expect((drawn.candles.at(-1) as { time: number }).time).toBe((fourteen - hour) / 1000);

    drawn.logicalRange = { from: 3, to: 120 };
    act(() => drawn.rangeChange?.({ from: 3, to: 120 }));

    await waitFor(() => expect(drawn.candles).toHaveLength(1000));
    expect(chartQueries(requests).at(-1)!.get('before')).toBe(String(fourteen - 500 * hour));
    expect(drawn.logicalRange).toEqual({ from: 503, to: 620 });
  });
});

// Task 18.20: «история загружается» and a failed page of the chart.
describe('the chart while the history loads or fails', () => {
  const loadingChart: FakeRoute = (request) =>
    json({
      symbol: 'SOLUSDT',
      timeframe: new URL(request.url).searchParams.get('timeframe'),
      loading: true,
      candles: [],
      openInterest: [],
      funding: [],
    });

  it('asks again every 5 seconds while the history loads', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let loading = true;
      const requests = routes([], {
        'GET /api/market/pairs/SOLUSDT/chart': (request) =>
          loading ? loadingChart(request) : chartRoute()(request),
      });
      renderApp('/market/SOLUSDT?timeframe=1h');
      expect(await screen.findByText('Сервис данных догружает историю пары…')).toBeInTheDocument();

      loading = false;
      await act(() => vi.advanceTimersByTimeAsync(5_100));

      await waitFor(() => expect(drawn.candles).toHaveLength(500));
      expect(chartQueries(requests).length).toBeGreaterThanOrEqual(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('goes on loading earlier pages after an early page answered that the history loads', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let early = 0;
      routes([], {
        'GET /api/market/pairs/SOLUSDT/chart': (request) => {
          if (new URL(request.url).searchParams.has('before') && early++ === 0) {
            return loadingChart(request);
          }

          return chartRoute()(request);
        },
      });
      renderApp('/market/SOLUSDT?timeframe=1h');
      await waitFor(() => expect(drawn.candles).toHaveLength(500));

      drawn.logicalRange = { from: 3, to: 120 };
      act(() => drawn.rangeChange?.({ from: 3, to: 120 }));
      await waitFor(() => expect(early).toBe(1));
      await act(() => vi.advanceTimersByTimeAsync(5_100));

      await waitFor(() => expect(drawn.candles).toHaveLength(1000));
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows a failed early page and asks it again by the button, not by every scroll', async () => {
    let fail = true;
    const requests = routes([], {
      'GET /api/market/pairs/SOLUSDT/chart': (request) =>
        new URL(request.url).searchParams.has('before') && fail
          ? json({ title: 'сервис данных не ответил' }, 502)
          : chartRoute()(request),
    });
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));

    drawn.logicalRange = { from: 3, to: 120 };
    act(() => drawn.rangeChange?.({ from: 3, to: 120 }));
    expect(await screen.findByRole('alert', { name: 'Ранние свечи' })).toBeInTheDocument();
    const asked = chartQueries(requests).length;
    act(() => drawn.rangeChange?.({ from: 2, to: 119 }));
    act(() => drawn.rangeChange?.({ from: 1, to: 118 }));
    expect(chartQueries(requests)).toHaveLength(asked);

    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));

    await waitFor(() => expect(drawn.candles).toHaveLength(1000));
    expect(screen.queryByRole('alert', { name: 'Ранние свечи' })).toBeNull();
  });
});

// Spec market-overview «Живая свеча» (task 14.3).
describe('the live candle', () => {
  const live = (
    openTime: number,
    close: number,
    closed = false,
    updatedAt = Date.UTC(2026, 10, 22, 14, 32, 5),
  ) => ({
    openTime,
    open: 236,
    high: Math.max(237, close),
    low: 235,
    close,
    volume: 12,
    closed,
    updatedAt,
  });

  // Scenario «Цена меняется».
  it('changes the last candle without reading the page again', async () => {
    routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));
    const loads = drawn.setData;

    act(() => FakeEventSource.last().send('candle', live(fourteen, 238.42)));

    await waitFor(() =>
      expect(drawn.updates.at(-1)).toMatchObject({
        time: fourteen / 1000,
        close: 238.42,
        high: 238.42,
      }),
    );
    expect(screen.getByRole('status', { name: 'Живая свеча' })).toHaveTextContent(
      'Живая свеча · 14:32:05 UTC',
    );
    expect(drawn.setData).toBe(loads);
  });

  // Scenario «Граница таймфрейма».
  it('closes the candle of 14:00 at 15:00 and starts the live one of 15:00', async () => {
    routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));

    act(() => FakeEventSource.last().send('candle', live(fourteen, 238.42, true)));
    act(() => FakeEventSource.last().send('candle', live(fourteen + hour, 238.5)));

    await waitFor(() =>
      expect(drawn.updates.at(-1)).toMatchObject({ time: (fourteen + hour) / 1000, close: 238.5 }),
    );
    expect(drawn.candles.at(-1)).toMatchObject({ time: fourteen / 1000, close: 238.42 });
    expect(drawn.candles).toHaveLength(501);
  });

  // Scenario «Поток оборвался».
  it('says the updates stopped and connects again', async () => {
    routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(FakeEventSource.opened).toHaveLength(1));

    act(() => FakeEventSource.last().fail());

    expect(screen.getByRole('status', { name: 'Живая свеча' })).toHaveTextContent(
      'Обновление остановилось',
    );
    expect(FakeEventSource.opened[0]!.closed).toBe(true);
    await waitFor(() => expect(FakeEventSource.opened).toHaveLength(2), { timeout: 4000 });
    act(() => FakeEventSource.last().send('candle', live(fourteen, 238)));
    expect(screen.getByRole('status', { name: 'Живая свеча' })).toHaveTextContent(
      'Живая свеча · 14:32:05 UTC',
    );
  });

  // Task 18.24: a closed candle missed while the stream was broken comes from the history read again.
  it('reads the history of the chart again after the stream connected again', async () => {
    const requests = routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));
    const asked = chartQueries(requests).length;

    act(() => FakeEventSource.last().fail());
    await waitFor(() => expect(FakeEventSource.opened).toHaveLength(2), { timeout: 4000 });
    act(() => FakeEventSource.last().send('candle', live(fourteen, 238)));

    await waitFor(() => expect(chartQueries(requests).length).toBeGreaterThan(asked));
  });

  it('reads the history of the chart again when the live candle steps over a candle', async () => {
    const requests = routes();
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.candles).toHaveLength(500));
    act(() => FakeEventSource.last().send('candle', live(fourteen, 238)));
    const asked = chartQueries(requests).length;

    act(() => FakeEventSource.last().send('candle', live(fourteen + hour, 238.5)));
    expect(chartQueries(requests)).toHaveLength(asked);
    act(() => FakeEventSource.last().send('candle', live(fourteen + 3 * hour, 239)));

    await waitFor(() => expect(chartQueries(requests).length).toBeGreaterThan(asked));
  });

  // Scenario «Страницу закрыли».
  it('closes the stream with the page', async () => {
    routes();
    const { unmount } = renderApp('/market/SOLUSDT');
    await waitFor(() => expect(FakeEventSource.opened).toHaveLength(1));

    unmount();

    expect(FakeEventSource.last().closed).toBe(true);
  });
});

// Spec market-overview «Идеи на графике» and «OI и фандинг под графиком» (task 14.4).
describe('the ideas, the open interest and the funding on the chart', () => {
  const shortClosed = idea({
    id: 'short-1',
    strategy: 'funding-short',
    version: 4,
    timeframe: '4h',
    side: 'short',
    state: 'closed',
    moment: Date.UTC(2026, 10, 21, 8),
    exitTime: Date.UTC(2026, 10, 21, 12),
    exitReason: 'time',
    result: 0.0041,
  });
  const longOpen = idea({ id: 'long-1', until: Date.UTC(2026, 10, 22, 17) });

  // Scenarios «Закрытая идея» and «Открытая идея».
  it('marks the entry and the exit of a closed idea and draws the lines of an open one', async () => {
    routes([longOpen, shortClosed]);
    renderApp('/market/SOLUSDT?timeframe=1h');

    await waitFor(() =>
      expect(drawn.markers).toEqual(
        expect.arrayContaining([
          expect.objectContaining({
            time: Date.UTC(2026, 10, 21, 8) / 1000,
            text: '▼ шорт · funding-short',
          }),
          expect.objectContaining({
            time: Date.UTC(2026, 10, 21, 12) / 1000,
            text: 'выход по сроку +0.41 %',
          }),
          expect.objectContaining({
            time: Date.UTC(2026, 10, 22, 13) / 1000,
            text: '▲ лонг · breakout-long',
          }),
        ]),
      ),
    );
    expect(drawn.lines.map((line) => [line.title, line.price])).toEqual([
      ['вход 236.10 · срок 17:00', 236.1],
      ['стоп 231.38', 231.38],
      ['цель 245.54', 245.54],
    ]);
  });

  // Scenario «Идея другого таймфрейма».
  it('puts the mark of an idea of 15m on the candle of 1h that its moment falls in', async () => {
    routes([
      idea({
        id: 'squeeze',
        timeframe: '15m',
        moment: Date.UTC(2026, 10, 22, 14, 15),
        state: 'closed',
        exitTime: Date.UTC(2026, 10, 22, 14, 30),
        exitReason: 'target',
        result: 0.01,
      }),
    ]);
    renderApp('/market/SOLUSDT?timeframe=1h');

    await waitFor(() =>
      expect(drawn.markers).toEqual(
        expect.arrayContaining([
          expect.objectContaining({ time: fourteen / 1000, text: '▲ лонг · breakout-long' }),
        ]),
      ),
    );
  });

  // Scenario «Клик по метке».
  it('chooses the row of the idea whose mark is clicked', async () => {
    routes([longOpen, shortClosed]);
    const { history } = renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.markers).toHaveLength(3));

    act(() => drawn.click?.({ time: Date.UTC(2026, 10, 21, 12) / 1000 }));

    await waitFor(() => expect(history.location.search).toContain('idea=short-1'));
    const table = within(screen.getByRole('table', { name: 'Идеи пары' }));
    expect(table.getByRole('row', { name: /short-1$/ })).toHaveAttribute('aria-selected', 'true');
    expect(table.getByRole('row', { name: /long-1$/ })).toHaveAttribute('aria-selected', 'false');
    expect(
      drawn.markers.filter((mark) => (mark as { color: string }).color === '#F28C28'),
    ).toHaveLength(2);
  });

  // Task 18.22, scenario «Идеи на графике»: the chart asks the ideas of its window, apart from the table and its choices.
  it('keeps the lines of an open idea when the table shows the closed ones only', async () => {
    const requests = routes([], {
      'GET /api/market/pairs/SOLUSDT/ideas': (request) => {
        const query = new URL(request.url).searchParams;
        const items = query.get('state') === 'closed' ? [shortClosed] : [longOpen, shortClosed];
        return json({ items, total: items.length, page: 1, pageSize: 50, filterGone: false });
      },
    });
    renderApp('/market/SOLUSDT?timeframe=1h');
    await waitFor(() => expect(drawn.lines).toHaveLength(3));

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Статус' }), 'closed');
    await waitFor(() =>
      expect(
        within(screen.getByRole('table', { name: 'Идеи пары' })).getAllByRole('row'),
      ).toHaveLength(2),
    );

    expect(drawn.lines).toHaveLength(3);
    const chart = requests
      .map((request) => new URL(request.url))
      .filter(
        (url) => url.pathname === '/api/market/pairs/SOLUSDT/ideas' && url.searchParams.has('from'),
      );
    expect(chart.length).toBeGreaterThan(0);
    expect(chart.every((url) => !url.searchParams.has('state'))).toBe(true);
    expect(Number(chart.at(-1)!.searchParams.get('from'))).toBe(fourteen - 500 * hour);
  });

  // Task 18.22, scenario strategy-ideas «Клик по идее»: the table opens on the page of the idea it was led to.
  it('opens the table on the page of the idea it was led to', async () => {
    const requests = routes([], {
      'GET /api/market/pairs/SOLUSDT/ideas': (request) => {
        const query = new URL(request.url).searchParams;
        const second = query.get('idea') === 'short-1' || query.get('page') === '2';
        const items = second ? [shortClosed] : [longOpen];
        return json({ items, total: 60, page: second ? 2 : 1, pageSize: 50, filterGone: false });
      },
    });
    const { history } = renderApp('/market/SOLUSDT?timeframe=4h&idea=short-1');

    const table = within(await screen.findByRole('table', { name: 'Идеи пары' }));
    expect(await table.findByRole('row', { name: /short-1$/ })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await waitFor(() => expect(history.location.search).toContain('page=2'));
    expect(
      requests
        .map((request) => new URL(request.url))
        .some(
          (url) =>
            url.pathname === '/api/market/pairs/SOLUSDT/ideas' &&
            url.searchParams.get('idea') === 'short-1',
        ),
    ).toBe(true);
  });

  // Scenarios «Панели под графиком» and «Отрицательная выплата».
  it('draws the open interest in USDT and the payments, a negative one red and downwards', async () => {
    routes();
    renderApp('/market/SOLUSDT?timeframe=1h');

    await waitFor(() => expect(drawn.series.Line).toHaveLength(500));
    expect(drawn.series.Line!.at(-1)).toEqual({
      time: (fourteen - hour) / 1000,
      value: 24_000 * 236,
    });
    expect(drawn.series.Histogram).toEqual([
      { time: (fourteen - 16 * hour) / 1000, value: 0.0001, color: '#3FB37F' },
      { time: (fourteen - 8 * hour) / 1000, value: -0.00004, color: '#E5484D' },
    ]);
    expect(screen.getByLabelText('OI под графиком')).toHaveTextContent('OI 6 млн');
    expect(screen.getByLabelText('Фандинг под графиком')).toHaveTextContent('Фандинг -0.0040 %');
  });
});

// Spec market-overview «Идеи пары» (task 14.5).
describe('the ideas of a pair', () => {
  // Scenario «Открытая и закрытые».
  it('has a row for each idea, the open one with «открыта» and the time left', async () => {
    routes([
      longOpen(),
      ...[1, 2, 3].map((day) =>
        idea({
          id: `closed-${day}`,
          state: 'closed',
          moment: Date.UTC(2026, 10, 22 - day, 8),
          exitTime: Date.UTC(2026, 10, 22 - day, 9),
          exitReason: 'time',
          result: 0.001,
        }),
      ),
    ]);
    renderApp('/market/SOLUSDT');

    const table = within(await screen.findByRole('table', { name: 'Идеи пары' }));
    expect(table.getAllByRole('row')).toHaveLength(5);
    expect(table.getByRole('row', { name: /long-1$/ })).toHaveTextContent(
      /открыта · \d+ ч \d+ мин/,
    );
    expect(screen.getByRole('heading', { name: /Идеи пары/ })).toHaveTextContent('4');
  });

  // Scenario «Состояние».
  it('asks only the open ideas of the pair when «открытые» is chosen', async () => {
    const requests = routes([longOpen()]);
    renderApp('/market/SOLUSDT');
    await screen.findByRole('table', { name: 'Идеи пары' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Статус' }), 'open');

    await waitFor(() =>
      expect(
        requests
          .map((request) => new URL(request.url))
          .filter(
            (url) =>
              url.pathname === '/api/market/pairs/SOLUSDT/ideas' && !url.searchParams.has('from'),
          )
          .at(-1)!
          .searchParams.get('state'),
      ).toBe('open'),
    );
  });

  // Scenario «Идей нет».
  it('says there were no ideas on the pair yet', async () => {
    routes();
    renderApp('/market/SOLUSDT');

    expect(await screen.findByText('Идей по этой паре ещё не было.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Идеи пары' })).toBeNull();
  });

  function longOpen() {
    return idea({ id: 'long-1' });
  }
});
