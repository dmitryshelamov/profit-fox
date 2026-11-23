import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

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
    'GET /api/ideas/state': () => json(scannerState()),
    'GET /api/ideas': ideaPage([idea()]),
    'GET /api/ideas/summary': () =>
      json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
    'GET /api/market/pairs/SOLUSDT': () => json({ symbol: 'SOLUSDT', ideas: [] }),
    ...extra,
  });
}

const cells = (row: HTMLElement) =>
  within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent);

// Spec strategy-ideas «Раздел «Идеи»» (change add-ideas-and-market, task 13.1).
describe('the section «Идеи»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Открытая идея».
  it('shows the pair, the strategy with its version, the timeframe, the side, the prices, the time left and «сейчас»', async () => {
    routes();
    renderApp('/ideas');

    const row = await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    expect(cells(row)).toEqual([
      'SOLUSDT',
      'breakout-longv7',
      '1h',
      'лонг',
      '—',
      '22.11 13:00',
      '236.10',
      '231.38',
      '245.54',
      '2 ч 28 мин',
      '+0.98 %',
    ]);
    expect(within(row).getByText('+0.98 %')).toHaveClass('text-ok');
    expect(screen.getByRole('tab', { name: /Открытые/ })).toHaveAttribute('aria-selected', 'true');
  });

  it('shows a result below zero in red', async () => {
    routes({ 'GET /api/ideas': ideaPage([idea({ now: -0.0053 })]) });
    renderApp('/ideas');

    const row = await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    expect(within(row).getByText('-0.53 %')).toHaveClass('text-err');
  });

  // Scenario «Идея без стопа».
  it('puts dashes in «Стоп» and «Цель» of an idea without them', async () => {
    routes({
      'GET /api/ideas': ideaPage([
        idea({ stop: null, target: null, stopFraction: null, targetFraction: null }),
      ]),
    });
    renderApp('/ideas');

    const row = await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    expect(cells(row).slice(7, 9)).toEqual(['—', '—']);
  });

  // Scenario «Журнал».
  it('puts the summary over the closed ideas, each with how it closed', async () => {
    const requests = routes({
      'GET /api/ideas/state': () => json(scannerState({ closed: 2 })),
      'GET /api/ideas': ideaPage([
        idea({
          id: 'a',
          symbol: 'AVAXUSDT',
          state: 'closed',
          exitTime: Date.UTC(2026, 10, 22, 12, 2),
          exitReason: 'target',
          result: 0.0289,
        }),
        idea({
          id: 'b',
          symbol: 'DOGEUSDT',
          state: 'closed',
          exitTime: Date.UTC(2026, 10, 22, 10, 14),
          exitReason: 'stop',
          result: -0.0211,
        }),
      ]),
      'GET /api/ideas/summary': () =>
        json({
          items: [
            {
              strategyId: 'strategy-breakout',
              strategy: 'breakout-long',
              version: 7,
              timeframe: '1h',
              ideas: 2,
              wins: 0.5,
              mean: 0.0039,
              sum: 0.78,
            },
          ],
          total: 1,
          page: 1,
          pageSize: 50,
          filterGone: false,
        }),
    });
    const { history } = renderApp('/ideas');
    await screen.findByRole('row', { name: 'AVAXUSDT breakout-long 1h' });

    await userEvent.click(screen.getByRole('tab', { name: /Журнал/ }));

    const summary = within(await screen.findByRole('table', { name: 'Сводка журнала' }));
    expect(cells(summary.getByRole('row', { name: 'breakout-long v7 1h' }))).toEqual([
      'breakout-longv7',
      '1h',
      '2',
      '50 %',
      '+0.39 %',
      '+0.78',
    ]);
    const closed = within(screen.getByRole('table', { name: 'Закрытые идеи' }));
    expect(
      within(closed.getByRole('row', { name: 'AVAXUSDT breakout-long 1h' })).getByText('по цели'),
    ).toBeInTheDocument();
    expect(
      within(closed.getByRole('row', { name: 'DOGEUSDT breakout-long 1h' })).getByText('по стопу'),
    ).toBeInTheDocument();
    expect(history.location.search).toContain('tab=journal');
    const journal = requests
      .map((request) => new URL(request.url))
      .filter((url) => url.pathname === '/api/ideas')
      .at(-1)!;
    expect([
      journal.searchParams.get('state'),
      journal.searchParams.get('sort'),
      journal.searchParams.get('period'),
    ]).toEqual(['closed', 'exitTime', '7']);
  });

  // Scenario «Клик по идее».
  it('opens the page of the pair on the timeframe of the idea with the idea chosen', async () => {
    routes();
    const { history } = renderApp('/ideas');

    await userEvent.click(await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' }));

    await waitFor(() => expect(history.location.pathname).toBe('/market/SOLUSDT'));
    expect(history.location.search).toContain('timeframe=1h');
    expect(history.location.search).toContain('idea=idea-sol');
  });

  // Scenario «Включённых стратегий нет».
  it('says the ideas come after a strategy is turned on and leads to «Стратегии»', async () => {
    routes({
      'GET /api/strategies': () =>
        json([{ ...enabled, ideas: { enabled: false, timeframes: [] } }]),
      'GET /api/ideas/state': () => json(scannerState({ open: 0, closed: 0 })),
    });
    renderApp('/ideas');

    const empty = (await screen.findByText(/Идеи появятся после включения стратегии/))
      .parentElement!;
    expect(within(empty).getByRole('link', { name: 'Стратегии' })).toHaveAttribute(
      'href',
      '/strategies',
    );
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('asks the open ideas with the choices and from the newest', async () => {
    const requests = routes();
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'short');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Только со стопом' }));

    await waitFor(() => {
      const query = requests
        .map((request) => new URL(request.url))
        .filter((url) => url.pathname === '/api/ideas')
        .at(-1)!.searchParams;
      expect([
        query.get('state'),
        query.get('sort'),
        query.get('direction'),
        query.get('side'),
        query.get('withStop'),
      ]).toEqual(['open', 'moment', 'desc', 'short', 'true']);
    });
  });
});

// Task 19.15: the name of a strategy stays on one line: a long one is cut with an ellipsis and has the whole as a hint.
describe('the name of a strategy in the lists of ideas', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('keeps the name on one line, cut with an ellipsis, with the whole name as a hint', async () => {
    const name = 'pump-long-on-the-close-of-a-very-long-candle';
    routes({ 'GET /api/ideas': ideaPage([idea({ strategy: name })]) });
    renderApp('/ideas');

    const row = await screen.findByRole('row', { name: `SOLUSDT ${name} 1h` });
    const shown = within(row).getByTitle(name);
    expect(shown).toHaveTextContent(name);
    expect(shown).toHaveClass('truncate');
    expect(shown.className).toMatch(/max-w-/);
    expect(within(row).getByText('v7')).toHaveClass('shrink-0');
  });
});

// Task 20.8: the tables of ideas fit their frame: the column of the strategy gives way, a time and a number with its
// percent stay on one line.
describe('the widths of the tables of ideas', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lets the strategy give way and keeps the times and the numbers of the open ideas on one line', async () => {
    routes();
    renderApp('/ideas');

    const table = await screen.findByRole('table', { name: 'Открытые идеи' });
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(2));
    const heads = within(table)
      .getAllByRole('columnheader')
      .map((head) => head.textContent);
    const cell = (title: string) =>
      within(within(table).getAllByRole('row')[1]!).getAllByRole('cell')[
        heads.findIndex((head) => head?.startsWith(title))
      ]!;

    expect(cell('Стратегия')).toHaveClass('w-full', 'max-w-0');
    expect(within(cell('Пара')).getByTitle('SOLUSDT')).toHaveClass('truncate');
    for (const title of ['Появилась, UTC', 'Вход', 'Стоп', 'Цель', 'Осталось', 'Сейчас']) {
      expect(cell(title)).toHaveClass('whitespace-nowrap');
    }
  });
});

// Task 18.27: the summary goes back to its first page when a filter is chosen.
describe('the page of the summary', () => {
  it('goes back to the first page when a filter is chosen', async () => {
    const summaries = Array.from({ length: 60 }, (_, index) => ({
      strategyId: `s-${index}`,
      strategy: `s-${index}`,
      version: 1,
      timeframe: '1h',
      ideas: 1,
      wins: 1,
      mean: 0.001,
      sum: 0.1,
    }));
    const requests = routes({
      'GET /api/ideas/summary': (request) => {
        const page = Number(new URL(request.url).searchParams.get('page') ?? 1);
        return json({
          items: summaries.slice((page - 1) * 50, page * 50),
          total: 60,
          page,
          pageSize: 50,
          filterGone: false,
        });
      },
      'GET /api/filters': () =>
        json([
          {
            id: 'f-shorts',
            name: 'Шорты',
            place: 'ideas',
            conditions: [{ field: 'side', operator: 'eq', value: 'short', to: null }],
          },
        ]),
      'GET /api/filters/fields': () => json([]),
    });
    renderApp('/ideas?tab=journal');
    const summary = await screen.findByRole('table', { name: 'Сводка журнала' });
    const pagers = await screen.findAllByRole('navigation', { name: 'Страницы' });
    await userEvent.click(within(pagers[0]!).getByRole('button', { name: 'Следующая страница' }));
    await waitFor(() =>
      expect(within(summary).getByRole('row', { name: 's-55 v1 1h' })).toBeInTheDocument(),
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Шорты' }));

    const asked = requests
      .map((request) => new URL(request.url))
      .filter((url) => url.pathname === '/api/ideas/summary')
      .at(-1)!.searchParams;
    await waitFor(() =>
      expect(
        requests
          .map((request) => new URL(request.url))
          .filter((url) => url.pathname === '/api/ideas/summary')
          .at(-1)!
          .searchParams.get('filter'),
      ).toBe('f-shorts'),
    );
    expect(
      requests
        .map((request) => new URL(request.url))
        .filter((url) => url.pathname === '/api/ideas/summary')
        .at(-1)!
        .searchParams.get('page'),
    ).toBe('1');
    expect(asked).toBeDefined();
  });
});

// Spec strategy-ideas «Состояние сканера в разделе «Идеи»» (task 13.2).
describe('the state of the scanner in «Идеи»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Прогрев после деплоя».
  it('shows the warm-up while it goes', async () => {
    routes({ 'GET /api/ideas/state': () => json(scannerState({ warmed: 310, pairs: 831 })) });
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).toHaveTextContent(
      'идёт прогрев: 310 из 831 пар',
    );
  });

  // Task 18.3: a refused warm-up is shown instead of the warm-up.
  it('shows the reason the warm-up fails instead of the warm-up', async () => {
    routes({
      'GET /api/ideas/state': () =>
        json(
          scannerState({
            warmed: 0,
            pairs: 831,
            failed: 831,
            failure: 'The REST of the market-data service answered 400 to candles',
          }),
        ),
    });
    renderApp('/ideas');

    const banner = await screen.findByRole('status', { name: 'Живые данные' });
    expect(banner).toHaveTextContent(
      'прогрев не идёт у 831 из 831 пар: The REST of the market-data service answered 400 to candles',
    );
    expect(banner).not.toHaveTextContent('идёт прогрев');
  });

  // Scenario «Сканер не работает».
  it('warns that new ideas do not come when nobody keeps the live data for two minutes', async () => {
    routes({ 'GET /api/ideas/state': () => json(scannerState({ live: false, idleSeconds: 120 })) });
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).toHaveTextContent(
      'новые идеи не появляются',
    );
  });

  it('does not warn while the lease ran out less than a minute ago', async () => {
    routes({ 'GET /api/ideas/state': () => json(scannerState({ live: false, idleSeconds: 20 })) });
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).not.toHaveTextContent(
      'не появляются',
    );
  });

  // Scenario «Обычная работа».
  it('shows since when ideas count without a break', async () => {
    routes();
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).toHaveTextContent(
      'идеи считаются без перерыва с 22.11 09:15 UTC',
    );
  });
});
