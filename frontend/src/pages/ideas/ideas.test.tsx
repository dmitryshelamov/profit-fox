import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { timeLeft } from '@/pages/ideas/ScannerBanner';
import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea, ideaPage, scannerState } from '@/test/ideas';
import { pair } from '@/test/market';
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
      '22.11 16:00',
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
    // Change fix-pair-chart-and-local-time, task 3.3: the columns of moments name the zone of the browser.
    expect(
      closed.getAllByRole('columnheader').map((cell) => cell.textContent?.replace(/[↕↑↓]$/, '')),
    ).toEqual(expect.arrayContaining(['Вход, UTC+3', 'Выход, UTC+3']));
    expect(
      cells(closed.getByRole('row', { name: 'AVAXUSDT breakout-long 1h' })).filter((text) =>
        /^\d\d\.\d\d \d\d:\d\d$/.test(text ?? ''),
      ),
    ).toEqual(['22.11 16:01', '22.11 15:02']);
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
    for (const title of ['Появилась, UTC+3', 'Вход', 'Стоп', 'Цель', 'Осталось', 'Сейчас']) {
      expect(cell(title)).toHaveClass('whitespace-nowrap');
    }
  });
});

// Task 21.3: in the tables of «Идеи» the name of a strategy keeps 14 characters and its version; the room comes from the
// labels, of which a row shows one and «+N» with the whole list in the hint.
describe('the strategy and the labels in the tables of «Идеи»', () => {
  afterEach(() => vi.unstubAllGlobals());

  const fourLabels = idea({
    strategy: 'pump-long-fast',
    labels: ['⚡ всплеск', '↓ BTC', '↓ пара', 'фандинг'],
  });

  async function cellsOf(tableName: string) {
    const table = await screen.findByRole('table', { name: tableName });
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(2));
    const heads = within(table)
      .getAllByRole('columnheader')
      .map((head) => head.textContent);
    return (title: string) =>
      within(within(table).getAllByRole('row')[1]!).getAllByRole('cell')[
        heads.findIndex((head) => head?.startsWith(title))
      ]!;
  }

  for (const [where, address, tableName] of [
    ['open ideas', '/ideas', 'Открытые идеи'],
    ['journal', '/ideas?tab=journal', 'Закрытые идеи'],
  ] as const) {
    it(`keeps 14 characters of the strategy and shows one label and «+N» in the ${where}`, async () => {
      routes({ 'GET /api/ideas': ideaPage([fourLabels]) });
      renderApp(address);
      const cell = await cellsOf(tableName);

      expect(cell('Стратегия')).toHaveClass(
        'w-full',
        'max-w-0',
        'font-mono',
        'min-w-[calc(14ch+1.25rem+0.5rem+2.25rem)]',
      );
      const labels = within(cell('Метки'));
      expect(labels.getByText('⚡ всплеск')).toBeInTheDocument();
      expect(labels.queryByText('↓ BTC')).toBeNull();
      expect(labels.getByText('+3')).toBeInTheDocument();
      expect(labels.getByTitle('⚡ всплеск, ↓ BTC, ↓ пара, фандинг')).toBeInTheDocument();
    });
  }
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

  it('counts the time left in whole minutes rounded up', () => {
    expect(timeLeft(42)).toBe('осталось меньше минуты');
    expect(timeLeft(60)).toBe('осталось ≈ 1 мин');
    expect(timeLeft(61)).toBe('осталось ≈ 2 мин');
    expect(timeLeft(3900)).toBe('осталось ≈ 1 ч 05 мин');
  });

  it('shows how long the warm-up has still to go', async () => {
    routes({
      'GET /api/ideas/state': () =>
        json(scannerState({ warmed: 310, pairs: 831, warmupLeftSeconds: 512.4 })),
    });
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).toHaveTextContent(
      'идёт прогрев: 310 из 831 пар · осталось ≈ 9 мин',
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

  // Task 24.1, scenario «Лента молчит»: the lease holds, but the feed tells no minute — ideas do not count without a break.
  it('says the feed catches up or is silent while ideas do not count without a break', async () => {
    routes({ 'GET /api/ideas/state': () => json(scannerState({ liveSince: null })) });
    renderApp('/ideas');

    const banner = await screen.findByRole('status', { name: 'Живые данные' });
    expect(banner).toHaveTextContent(
      'лента рынка догоняет или молчит: новые идеи появятся, когда она дойдёт до текущей минуты',
    );
    expect(banner).not.toHaveTextContent('без перерыва');
  });

  // Scenario «Обычная работа».
  it('shows since when ideas count without a break', async () => {
    routes();
    renderApp('/ideas');

    expect(await screen.findByRole('status', { name: 'Живые данные' })).toHaveTextContent(
      'идеи считаются без перерыва с 22.11 12:15',
    );
  });
});

// Change add-setup-strategies, task 5.2: spec strategy-ideas «Ряд стратегий в разделе „Идеи“».
describe('the row of strategies in «Идеи»', () => {
  afterEach(() => vi.unstubAllGlobals());

  /** The answer of the numbers by strategy: 17 of breakout, 35 of false-breakout and none of accumulation, or as given. */
  function counts(rows: readonly (readonly [string, number])[]) {
    return json({
      total: rows.reduce((sum, [, ideas]) => sum + ideas, 0),
      strategies: rows.map(([strategy, ideas]) => ({ strategy, ideas })),
      filterGone: false,
    });
  }

  const three = [
    ['accumulation', 0],
    ['breakout', 17],
    ['false-breakout', 35],
  ] as const;

  const queriesOf = (requests: readonly Request[], path: string) =>
    requests
      .map((request) => new URL(request.url))
      .filter((url) => url.pathname === path)
      .map((url) => url.searchParams);

  const chips = () =>
    within(screen.getByRole('group', { name: 'Стратегии' }))
      .getAllByRole('button')
      .map((chip) => [chip.textContent, chip.getAttribute('aria-pressed')]);

  // Scenario «Ряд над открытыми».
  it('puts «Все» and a chip of each strategy with its number over the open ideas, «Все» chosen', async () => {
    const requests = routes({ 'GET /api/ideas/strategies': () => counts(three) });
    renderApp('/ideas');

    await screen.findByRole('button', { name: 'breakout 17' });

    expect(chips()).toEqual([
      ['Все 52', 'true'],
      ['accumulation 0', 'false'],
      ['breakout 17', 'false'],
      ['false-breakout 35', 'false'],
    ]);
    expect(screen.queryByRole('combobox', { name: 'Стратегия' })).toBeNull();
    expect(queriesOf(requests, '/api/ideas/strategies').at(-1)!.get('state')).toBe('open');
    const group = screen.getByRole('group', { name: 'Стратегии' });
    const table = screen.getByRole('table', { name: 'Открытые идеи' });
    expect(group.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(group).toHaveClass('flex-wrap');
  });

  // Scenario «Выбор стратегии».
  it('chooses the ideas of a strategy from the first page and leaves the numbers as they were', async () => {
    const requests = routes({ 'GET /api/ideas/strategies': () => counts(three) });
    renderApp('/ideas?page=3');
    await screen.findByRole('button', { name: 'breakout 17' });
    const asked = queriesOf(requests, '/api/ideas/strategies').length;

    await userEvent.click(screen.getByRole('button', { name: 'breakout 17' }));

    await waitFor(() => {
      const query = queriesOf(requests, '/api/ideas').at(-1)!;
      expect([query.get('strategy'), query.get('page')]).toEqual(['breakout', '1']);
    });
    expect(screen.getByRole('button', { name: 'breakout 17' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Все 52' })).toHaveAttribute('aria-pressed', 'false');
    expect(chips().map(([words]) => words)).toEqual([
      'Все 52',
      'accumulation 0',
      'breakout 17',
      'false-breakout 35',
    ]);
    expect(queriesOf(requests, '/api/ideas/strategies')).toHaveLength(asked);
    expect(
      queriesOf(requests, '/api/ideas/strategies').every((query) => !query.has('strategy')),
    ).toBe(true);
  });

  // Scenario «Снятие выбора».
  it('takes the choice off on the chosen chip again and on «Все»', async () => {
    const requests = routes({ 'GET /api/ideas/strategies': () => counts(three) });
    renderApp('/ideas');
    const breakout = await screen.findByRole('button', { name: 'breakout 17' });

    await userEvent.click(breakout);
    await userEvent.click(screen.getByRole('button', { name: 'breakout 17' }));

    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas').at(-1)!.has('strategy')).toBe(false),
    );
    expect(screen.getByRole('button', { name: 'Все 52' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'false-breakout 35' }));
    await userEvent.click(screen.getByRole('button', { name: 'Все 52' }));

    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas').at(-1)!.has('strategy')).toBe(false),
    );
    expect(screen.getByRole('button', { name: 'false-breakout 35' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  // Scenario «Другой выбор меняет числа».
  it('counts with the other choices: the short side gives the numbers of the shorts', async () => {
    const requests = routes({
      'GET /api/ideas/strategies': (request) =>
        new URL(request.url).searchParams.get('side') === 'short'
          ? counts([
              ['accumulation', 0],
              ['breakout', 4],
              ['false-breakout', 9],
            ])
          : counts(three),
    });
    renderApp('/ideas');
    await screen.findByRole('button', { name: 'breakout 17' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'short');

    expect(await screen.findByRole('button', { name: 'breakout 4' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Все 13' })).toBeInTheDocument();
    expect(queriesOf(requests, '/api/ideas/strategies').at(-1)!.get('side')).toBe('short');
  });

  // Scenario «Журнал».
  it('chooses the strategy of the summary and of the closed ideas of the journal over its period', async () => {
    const requests = routes({
      'GET /api/ideas/state': () => json(scannerState({ closed: 52 })),
      'GET /api/ideas/strategies': () => counts(three),
    });
    renderApp('/ideas?tab=journal');
    await screen.findByRole('button', { name: 'breakout 17' });

    await userEvent.click(screen.getByRole('button', { name: 'breakout 17' }));

    await waitFor(() => {
      for (const path of ['/api/ideas', '/api/ideas/summary']) {
        const query = queriesOf(requests, path).at(-1)!;
        expect([path, query.get('strategy'), query.get('period')]).toEqual([path, 'breakout', '7']);
      }
    });
    const query = queriesOf(requests, '/api/ideas/strategies').at(-1)!;
    expect([query.get('state'), query.get('period'), query.has('strategy')]).toEqual([
      'closed',
      '7',
      false,
    ]);
    const group = screen.getByRole('group', { name: 'Стратегии' });
    for (const name of ['Сводка журнала', 'Закрытые идеи']) {
      const table = screen.getByRole('table', { name });
      expect(group.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
  });

  // Scenario «Страница пары».
  it('keeps the list «Стратегия: все» over the ideas of a pair and no row of strategies', async () => {
    const requests = routes({
      'GET /api/ideas/strategies': () => counts(three),
      'GET /api/market/pairs/SOLUSDT': () => json(pair('SOLUSDT')),
      'GET /api/market/pairs/SOLUSDT/chart': () =>
        json({ symbol: 'SOLUSDT', timeframe: '1h', loading: false, candles: [], funding: [] }),
      'GET /api/market/pairs/SOLUSDT/chart/open-interest': () =>
        json({ symbol: 'SOLUSDT', timeframe: '1h', loading: false, openInterest: [] }),
      'GET /api/market/pairs/SOLUSDT/ideas': () =>
        json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
    });
    renderApp('/market/SOLUSDT');

    const strategy = await screen.findByRole('combobox', { name: 'Стратегия' });
    expect(within(strategy).getByRole('option', { name: 'Стратегия: все' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Стратегии' })).toBeNull();
    expect(queriesOf(requests, '/api/ideas/strategies')).toHaveLength(0);
  });
});
