import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Run, RunSet } from '@/api/runs';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { backtest, numbersOf } from '@/test/runs';

import { todayUtc } from './RunFormPage';

const going = backtest({
  id: 'r1',
  state: 'running',
  worker: 'soyo-02',
  startedAt: '2026-11-20T08:01:00Z',
  pairsDone: 414,
  pairsTotal: 828,
});

/** A set of `funding-short` with a threshold and a timeframe; numbers only of a done run. */
function set(
  number: number,
  threshold: string,
  timeframe: string,
  numbers?: Partial<RunSet['numbers']>,
) {
  return {
    number,
    timeframe,
    values: [{ name: 'threshold', value: threshold }],
    numbers: numbers === undefined ? null : numbersOf(numbers ?? {}),
  };
}

/** The six sets of three thresholds on 1h and 4h, done. */
const six = [
  set(1, '0.0003', '1h', {
    trades: 40,
    pairsWithTrades: 12,
    mean: 0.001,
    amount: 4.1,
    winShare: 0.55,
  }),
  set(2, '0.0003', '4h', {
    trades: 31,
    pairsWithTrades: 10,
    mean: -0.002,
    amount: -6.2,
    winShare: 0.4,
  }),
  set(3, '0.0005', '1h', {
    trades: 22,
    pairsWithTrades: 8,
    mean: 0.004,
    amount: 8.8,
    winShare: 0.6,
  }),
  set(4, '0.0005', '4h', {
    trades: 18,
    pairsWithTrades: 7,
    mean: 0.006,
    amount: 10.8,
    winShare: 0.61,
    difference: { mean: 0.003, weeks: 20, t: 1.7 },
  }),
  set(5, '0.001', '1h', { trades: 5, pairsWithTrades: 3, mean: 0.002, amount: 1, winShare: 0.6 }),
  set(6, '0.001', '4h', { trades: 0 }),
];

const doneSix = backtest({
  id: 'r1',
  state: 'done',
  settings: {
    values: [{ name: 'threshold', values: ['0.0003', '0.0005', '0.001'] }],
    timeframes: ['1h', '4h'],
    start: '2023-01-01',
    end: '2026-06-01',
    pairs: null,
    takerFee: 0.00055,
    makerFee: 0.0002,
  },
  startedAt: '2026-11-20T08:01:00Z',
  finishedAt: '2026-11-20T08:14:00Z',
  worker: 'soyo-02',
  pairsDone: 828,
  pairsTotal: 828,
  setCount: 6,
  sets: six,
  engineVersion: 3,
  dataHash: '8c33b68603417c3865a5914ad872af25a83b7ab3b505ab25f645b0d6c4bab201',
  readSeconds: 420,
  computeSeconds: 754,
  bestAmount: 10.8,
});

/** The queue and the finished runs of `GET /api/backtests`. */
const lists = (active: readonly unknown[], finished: readonly unknown[] = []) => ({
  'GET /api/backtests': (request: Request) =>
    json(new URL(request.url).searchParams.get('active') === 'true' ? active : finished),
});

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({ ...baseRoutes(), ...lists([]), ...extra });
}

// Spec bench-run-queue «Страница «Прогоны»».
describe('the section «Прогоны»', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario «Прогон закончился, пока страница открыта».
  it('moves a run that ended while the page is open to the finished ones', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let ended = false;
    fakeApi({
      'GET /api/backtests': (request) =>
        new URL(request.url).searchParams.get('active') === 'true'
          ? json(ended ? [] : [going])
          : json(ended ? [doneSix] : []),
    });
    renderApp('/runs');

    const run = await screen.findByRole('listitem', { name: 'Прогон funding-short r1' });
    expect(run).toHaveTextContent('414 из 828 пар · 50 %');
    expect(run).toHaveTextContent('soyo-02');
    expect(run).toHaveTextContent('v3');
    expect(run).toHaveTextContent('категория идей');
    expect(run).toHaveTextContent('4h · наборов 1 · все пары');
    expect(screen.queryByRole('row', { name: /Законченный/ })).toBeNull();

    ended = true;
    await act(() => vi.advanceTimersByTimeAsync(3100));

    const done = await screen.findByRole('row', { name: 'Законченный funding-short r1' });
    expect(within(done).getByText('Готов')).toHaveAttribute('data-tone', 'ok');
    expect(done).toHaveTextContent('2023-01-01 — 2026-06-01');
    expect(done).toHaveTextContent('+10.80');
    expect(done).toHaveTextContent('754 с');
    expect(screen.queryByRole('listitem', { name: 'Прогон funding-short r1' })).toBeNull();
    expect(screen.getByText('Очередь пуста.')).toBeInTheDocument();
  });

  it('cancels a run with «Отменить»', async () => {
    const requests = fakeApi({
      ...lists([backtest({ id: 'r2' })]),
      'POST /api/backtests/r2/cancel': () => new Response(null, { status: 204 }),
    });
    renderApp('/runs');

    const waiting = await screen.findByRole('listitem', { name: 'Прогон funding-short r2' });
    expect(within(waiting).getByText('В очереди')).toHaveAttribute('data-tone', 'neutral');
    await userEvent.click(within(waiting).getByRole('button', { name: 'Отменить' }));

    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith('/api/backtests/r2/cancel'))).toBe(
        true,
      ),
    );
  });

  // Spec bench-run-queue «Ход идущего прогона полосой».
  it('fills the bar of a run that goes and says where a run that waits stands', async () => {
    fakeApi(
      lists([
        going,
        backtest({ id: 'r2', settings: { ...going.settings, pairs: ['BTCUSDT', 'ETHUSDT'] } }),
        backtest({ id: 'r3' }),
      ]),
    );
    renderApp('/runs');

    const run = within(await screen.findByRole('listitem', { name: 'Прогон funding-short r1' }));
    const bar = run.getByRole('progressbar', { name: 'Ход прогона' });
    expect(bar).toHaveAttribute('aria-valuenow', '50');
    expect(run.getByText('Идёт')).toHaveAttribute('data-tone', 'run');
    expect(run.getByText(/soyo-02 · с 11:01/)).toBeInTheDocument();
    expect(run.queryByText(/UTC/)).toBeNull();
    const next = within(screen.getByRole('listitem', { name: 'Прогон funding-short r2' }));
    expect(next.queryByRole('progressbar')).toBeNull();
    expect(next.getByText('ждёт, начнётся после текущего')).toBeInTheDocument();
    expect(next.getByText(/BTCUSDT, ETHUSDT/)).toBeInTheDocument();
    const last = within(screen.getByRole('listitem', { name: 'Прогон funding-short r3' }));
    expect(last.getByText('ждёт, перед ним в очереди 1')).toBeInTheDocument();
  });
});

// Spec bench-run-queue «Строка законченного прогона»; spec bench-ui «Цвет состояния прогона».
describe('the row of a finished run', () => {
  afterEach(() => vi.unstubAllGlobals());

  const long =
    'история недоступна: сервис рыночных данных не отдал минутные свечи — соединение разорвано на 312-й паре из 831, повтор чтения минутных свечей и фандинга не помог, и прогон остановлен до чисел наборов.';
  const finished = [
    backtest({ id: 'r9', state: 'failed', finishedAt: '2026-11-21T02:14:00Z', reason: long }),
    backtest({ id: 'r8', state: 'cancelled', finishedAt: '2026-11-21T01:40:00Z' }),
    { ...doneSix, id: 'r7', finishedAt: '2026-11-21T00:01:00Z' },
  ];

  // Scenarios «Длинная причина» and «Упавший и готовый прогоны».
  it('keeps a long reason on one line with the whole text in the hint, and colors the state', async () => {
    expect(long.length).toBeGreaterThanOrEqual(200);
    routes(lists([], finished));
    renderApp('/runs');

    const failed = await screen.findByRole('row', { name: 'Законченный funding-short r9' });
    expect(failed).toHaveAttribute('title', long);
    expect(within(failed).getByText(long).className).toContain('truncate');
    expect(within(failed).getByText('Упал')).toHaveAttribute('data-tone', 'err');
    const cancelled = screen.getByRole('row', { name: 'Законченный funding-short r8' });
    expect(within(cancelled).getByText('Отменён')).toHaveAttribute('data-tone', 'neutral');
    const done = screen.getByRole('row', { name: 'Законченный funding-short r7' });
    expect(within(done).getByText('Готов')).toHaveAttribute('data-tone', 'ok');
    expect(failed.className).toBe(done.className);
    expect(
      screen
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent)
        .slice(0, 8),
    ).toEqual([
      'Стратегия',
      'Состояние',
      'История',
      'Наборов',
      'Лучшая сумма',
      'Причина',
      'Расчёт',
      'Закончен',
    ]);
    expect(done).toHaveTextContent('21.11 03:01');
  });

  // Scenario «Открытие с клавиатуры».
  it('opens the page of the run by Enter on the focused row', async () => {
    routes({ ...lists([], finished), 'GET /api/backtests/r8': () => json(finished[1]) });
    const { history } = renderApp('/runs');

    const row = await screen.findByRole('row', { name: 'Законченный funding-short r8' });
    const link = within(row).getByRole('link', { name: /funding-short/ });
    expect(link).toHaveAttribute('href', '/runs/r8');
    expect(link.className).toContain('after:inset-0');
    link.focus();
    await userEvent.keyboard('{Enter}');

    await waitFor(() => expect(history.location.pathname).toBe('/runs/r8'));
  });
});

/** The rows of the table of sets by their number, top to bottom. */
function setOrder(): string[] {
  const table = screen.getByRole('table', { name: 'Наборы прогона' });
  return within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.getAttribute('aria-label')!.replace('Набор ', ''));
}

// Spec bench-run-queue «Страница прогона», «Таблица наборов прогона», «Хэши прогона копируются».
describe('the page of a run', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the settings, the engine, the hash of the data, the times and the warning', async () => {
    routes({ 'GET /api/backtests/r1': () => json(doneSix) });
    renderApp('/runs/r1');

    expect(await screen.findByRole('heading', { name: 'funding-short' })).toBeInTheDocument();
    expect(screen.getByText('версия 3')).toBeInTheDocument();
    expect(screen.getByLabelText('Вид')).toHaveTextContent('категория идей');
    const settings = within(screen.getByRole('region', { name: 'Настройки' }));
    expect(settings.getByText('1h, 4h')).toBeInTheDocument();
    expect(settings.getByText('0.0003, 0.0005, 0.001')).toBeInTheDocument();
    expect(settings.getByText('2023-01-01 — 2026-06-01')).toBeInTheDocument();
    expect(settings.getByText('все пары')).toBeInTheDocument();
    expect(settings.getByText('тейкер 0.00055 · мейкер 0.0002')).toBeInTheDocument();
    const details = within(screen.getByRole('region', { name: 'Детали' }));
    expect(details.getByText('3')).toBeInTheDocument();
    expect(details.getByText(/^420 с/)).toBeInTheDocument();
    expect(details.getByText(/^754 с/)).toBeInTheDocument();
    expect(screen.getByLabelText('Предупреждение')).toHaveTextContent('Выжившие пары');
  });

  // Task 12.10: a time under a minute is written once, a longer one with its minutes.
  it('writes a time under a minute once and a longer one with its minutes', async () => {
    routes({
      'GET /api/backtests/r1': () => json({ ...doneSix, readSeconds: 14, computeSeconds: 754 }),
    });
    renderApp('/runs/r1');

    const details = within(await screen.findByRole('region', { name: 'Детали' }));
    expect(details.getByText('14 с')).toBeInTheDocument();
    expect(details.queryByText('14 с (14 с)')).toBeNull();
    expect(details.getByText('754 с (12 мин 34 с)')).toBeInTheDocument();
  });

  // Scenario «Копирование хэша данных».
  it('shows the hash of the data by its ends and copies it whole', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    routes({ 'GET /api/backtests/r1': () => json(doneSix) });
    renderApp('/runs/r1');

    const details = within(await screen.findByRole('region', { name: 'Детали' }));
    expect(details.getByText('8c33b686…c4bab201')).toBeInTheDocument();
    await userEvent.click(details.getByRole('button', { name: 'Скопировать: Хэш данных' }));

    expect(writeText).toHaveBeenCalledWith(doneSix.dataHash);
    Reflect.deleteProperty(navigator, 'clipboard');
  });

  // Scenario «Сортировка по среднему».
  it('sorts the sets by the sum first, and by any column on a click on its head', async () => {
    routes({ 'GET /api/backtests/r1': () => json(doneSix) });
    renderApp('/runs/r1');
    await screen.findByRole('table', { name: 'Наборы прогона' });

    expect(setOrder()).toEqual(['4', '3', '1', '5', '6', '2']);
    await userEvent.click(screen.getByRole('button', { name: 'Средний результат' }));
    expect(setOrder()).toEqual(['4', '3', '5', '1', '2', '6']);
    await userEvent.click(screen.getByRole('button', { name: /Средний результат/ }));
    expect(setOrder()).toEqual(['2', '1', '5', '3', '4', '6']);
    expect(screen.getByRole('columnheader', { name: /Средний результат/ })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
  });

  // Scenario «Набор без сделок».
  it('puts dashes for the numbers a set without trades has not', async () => {
    routes({ 'GET /api/backtests/r1': () => json(doneSix) });
    renderApp('/runs/r1');

    const row = within(await screen.findByRole('row', { name: 'Набор 6' }));
    const cells = row.getAllByRole('cell').map((cell) => cell.textContent);
    const heads = screen
      .getAllByRole('columnheader')
      .map((cell) => cell.textContent!.replace(/[↓↑]/, '').trim());
    const at = (title: string) => cells[heads.indexOf(title)];
    expect(at('Сделок')).toBe('0');
    expect(at('Средний результат')).toBe('—');
    expect(at('Прибыльных')).toBe('—');
    expect(at('Разница')).toBe('—');
    expect(at('t')).toBe('—');
  });

  // Scenario «Лесенка без разницы».
  it('has no columns of the difference for a strategy of a bot', async () => {
    const ladder = {
      ...doneSix,
      strategy: { ...doneSix.strategy, name: 'ladder', kind: 'bot' },
    };
    routes({ 'GET /api/backtests/r1': () => json(ladder) });
    renderApp('/runs/r1');

    const table = within(await screen.findByRole('table', { name: 'Наборы прогона' }));
    const heads = table.getAllByRole('columnheader').map((cell) => cell.textContent);
    expect(heads).toContain('Сумма, USDT↓');
    expect(heads.some((head) => head?.startsWith('Разница'))).toBe(false);
    expect(heads.some((head) => head === 't')).toBe(false);
  });

  // Scenario «Двести наборов».
  it('scrolls 200 sets inside the table under a head that stays', async () => {
    const many = Array.from({ length: 200 }, (_, index) =>
      set(index + 1, String(index), '4h', { trades: index, amount: index }),
    );
    routes({
      'GET /api/backtests/r1': () => json({ ...doneSix, setCount: 200, sets: many }),
    });
    renderApp('/runs/r1');

    const table = await screen.findByRole('table', { name: 'Наборы прогона' });
    expect(within(table).getAllByRole('row')).toHaveLength(201);
    expect(table.parentElement!.className).toContain('overflow-y-auto');
    expect(table.parentElement!.className).toContain('max-h-');
    expect(table.querySelector('thead')!.className).toContain('sticky');
  });

  // Scenario «Упавший прогон».
  it('shows the whole reason of a failed run and no table of sets', async () => {
    const reason = 'история недоступна: the market data service answered 503';
    routes({
      'GET /api/backtests/r4': () => json(backtest({ id: 'r4', state: 'failed', reason })),
    });
    renderApp('/runs/r4');

    expect(await screen.findByRole('status')).toHaveTextContent(`Прогон упал: ${reason}`);
    expect(screen.queryByRole('region', { name: 'Наборы' })).toBeNull();
  });

  it('opens a run that goes as the API sends it, without the fields that have no value yet', async () => {
    const going = backtest({
      id: 'r6',
      state: 'running',
      startedAt: '2026-11-21T00:10:01Z',
      worker: 'Mac-1',
    });
    routes({ 'GET /api/backtests/r6': () => json(withoutNulls(going)) });
    renderApp('/runs/r6');

    expect(await screen.findByText('Поставлен')).toBeInTheDocument();
    // Change fix-pair-chart-and-local-time, task 3.2: in the zone of the browser, the zone named.
    expect(screen.getByText('Поставлен').parentElement).toHaveTextContent('2026-11-21 03:10 UTC+3');
    expect(screen.getByText('Закончен').parentElement).toHaveTextContent('—');
    expect(screen.queryByText(/Invalid time value/)).toBeNull();
  });

  // Task 12.5: a waiting run cancelled in another tab is removed; the page stops asking for it and does not say it waits.
  it('stops asking for a run that is gone and does not say it waits', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let gone = false;
    const requests = routes({
      'GET /api/backtests/r6': () =>
        gone ? json({ title: 'Not Found' }, 404) : json(backtest({ id: 'r6' })),
    });
    renderApp('/runs/r6');

    expect(await screen.findByRole('status')).toHaveTextContent('Прогон ждёт своей очереди.');
    gone = true;
    await act(() => vi.advanceTimersByTimeAsync(3100 * 3));

    expect(await screen.findByText('Прогона r6 нет.')).toBeInTheDocument();
    expect(screen.queryByText('Прогон ждёт своей очереди.')).toBeNull();
    const asked = requests.filter((request) => request.url.endsWith('/api/backtests/r6')).length;
    await act(() => vi.advanceTimersByTimeAsync(3100 * 3));
    expect(requests.filter((request) => request.url.endsWith('/api/backtests/r6'))).toHaveLength(
      asked,
    );
  });

  it('says that a run waits, and leads from a run of the rest of history to its source', async () => {
    routes({
      'GET /api/backtests/r5': () =>
        json(backtest({ id: 'r5', source: { backtest: 'r1', set: 4 } })),
    });
    renderApp('/runs/r5');

    expect(await screen.findByRole('status')).toHaveTextContent('Прогон ждёт своей очереди.');
    expect(screen.getByRole('link', { name: /Остаток истории набора 4/ })).toHaveAttribute(
      'href',
      '/runs/r1',
    );
  });
});

/** A body as the API writes it: a field without a value is left out, not written as `null`. */
function withoutNulls(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(withoutNulls);
  }
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([, field]) => field !== null)
        .map(([name, field]) => [name, withoutNulls(field)]),
    );
  }
  return value;
}

/** The pairs of set 4 of `doneSix`. */
const pairsOfFour = {
  set: six[3],
  pairs: [
    {
      pair: 'BTCUSDT',
      numbers: numbersOf({ orders: 3, trades: 2, mean: -0.0077, amount: -1.54, winShare: 0 }),
    },
    {
      pair: 'ETHUSDT',
      numbers: numbersOf({ orders: 5, trades: 4, mean: 0.01, amount: 4, winShare: 0.75 }),
    },
  ],
};

// Spec bench-run-queue «Набор прогона на странице».
describe('the set of a run', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Пары набора».
  it('opens from its row with the trades, the mean and the sum of each pair', async () => {
    routes({
      'GET /api/backtests/r1': () => json(doneSix),
      'GET /api/backtests/r1/sets/4': () => json(pairsOfFour),
    });
    const { history } = renderApp('/runs/r1');

    const row = await screen.findByRole('row', { name: 'Набор 4' });
    expect(row).toHaveTextContent('0.0005');
    await userEvent.click(within(row).getByRole('link'));

    await waitFor(() => expect(history.location.pathname).toBe('/runs/r1/sets/4'));
    const table = within(await screen.findByRole('table', { name: 'Пары набора' }));
    const btc = within(table.getByRole('row', { name: 'Пара BTCUSDT' }));
    expect(btc.getByText('2')).toBeInTheDocument();
    expect(btc.getByText('-0.77 %')).toBeInTheDocument();
    expect(btc.getByText('-1.54')).toBeInTheDocument();
    // The best pair first.
    expect(
      table
        .getAllByRole('row')
        .slice(1)
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Пара ETHUSDT', 'Пара BTCUSDT']);
    expect(screen.getByRole('heading', { name: /Набор 4/ })).toHaveTextContent(
      '4h · threshold = 0.0005',
    );
  });

  // Scenario «Остаток истории».
  it('puts the set on the rest of history and opens the new run', async () => {
    const rest = backtest({
      id: 'r9',
      source: { backtest: 'r1', set: 4 },
      settings: { ...doneSix.settings, start: '2026-06-01', end: todayUtc() },
    });
    const requests = routes({
      'GET /api/backtests/r1': () => json(doneSix),
      'GET /api/backtests/r1/sets/4': () => json(pairsOfFour),
      'POST /api/backtests/r1/sets/4/rest': () => json(rest, 201),
      'GET /api/backtests/r9': () => json(rest),
    });
    const { history } = renderApp('/runs/r1/sets/4');

    const button = await screen.findByRole('button', { name: 'Прогнать на остатке истории' });
    expect(button).toBeEnabled();
    await userEvent.click(button);

    await waitFor(() => expect(history.location.pathname).toBe('/runs/r9'));
    expect(
      requests.filter((request) => request.method === 'POST').map((request) => request.url),
    ).toEqual([expect.stringMatching(/\/api\/backtests\/r1\/sets\/4\/rest$/)]);
  });

  // Scenario «Остатка нет».
  it('has no rest of history to run on when the run went to the start of the current day', async () => {
    const today = { ...doneSix, settings: { ...doneSix.settings, end: todayUtc() } };
    routes({
      'GET /api/backtests/r1': () => json(today),
      'GET /api/backtests/r1/sets/4': () => json(pairsOfFour),
    });
    renderApp('/runs/r1/sets/4');

    const button = await screen.findByRole('button', { name: 'Прогнать на остатке истории' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', 'остатка истории нет');
  });

  it('shows the pairs of a run of one set on the page of the run', async () => {
    const one: Run = { ...doneSix, setCount: 1, sets: [six[3]!] };
    routes({
      'GET /api/backtests/r1': () => json(one),
      'GET /api/backtests/r1/sets/4': () => json(pairsOfFour),
    });
    renderApp('/runs/r1');

    expect(await screen.findByRole('table', { name: 'Пары набора' })).toBeInTheDocument();
  });
});

/** The version 3 of `funding-short` with its parameter `threshold`. */
function strategyVersion(kind = 'idea', name = 'funding-short') {
  return {
    name,
    description: null,
    version: 3,
    kind,
    graph: {
      formatVersion: 1,
      parameters: [{ name: 'threshold', type: 'number', value: 0.0005 }],
      outputs: [],
      nodes: [],
      wires: [],
    },
    layout: {},
    signals: [],
    raised: false,
  };
}

// Spec bench-run-queue «Форма запуска прогона».
describe('the form of a run', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  const form = () => within(screen.getByRole('form', { name: 'Запуск прогона' }));

  // Scenario «Число наборов»; the count of the form is the count the server gives the run.
  it('counts six sets of three thresholds on 1h and 4h and opens the run, which has six', async () => {
    const queued = {
      ...doneSix,
      id: 'r7',
      state: 'queued',
      sets: six.map((item) => ({ ...item, numbers: null })),
    };
    const requests = routes({
      'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()),
      'POST /api/backtests': () => json(queued, 201),
      'GET /api/backtests/r7': () => json(queued),
    });
    const { history } = renderApp('/strategies/funding-short/run?version=3');

    const values = await screen.findByRole('textbox', { name: 'Значения threshold' });
    expect(values).toHaveValue('0.0005');
    expect(form().getByRole('status', { name: 'Наборов' })).toHaveTextContent('Наборов: 1');
    await userEvent.clear(values);
    await userEvent.type(values, '0.0003, 0.0005, 0.001');
    await userEvent.click(form().getByRole('checkbox', { name: '1h' }));

    expect(form().getByRole('status', { name: 'Наборов' })).toHaveTextContent('Наборов: 6');
    await userEvent.click(form().getByRole('button', { name: 'Поставить в очередь' }));

    await waitFor(() => expect(history.location.pathname).toBe('/runs/r7'));
    const [body] = (await bodiesOf(requests, 'POST', '/api/backtests')) as Record<
      string,
      unknown
    >[];
    expect(body).toMatchObject({
      strategy: 'funding-short',
      version: 3,
      values: { threshold: ['0.0003', '0.0005', '0.001'] },
      timeframes: ['1h', '4h'],
      pairs: null,
      takerFee: 0.00055,
      makerFee: 0.0002,
    });
    const settings = within(await screen.findByRole('region', { name: 'Настройки' }));
    expect(settings.getByText('6')).toBeInTheDocument();
  });

  // Scenario «Конец истории по умолчанию».
  it('ends the history at the current UTC day, and the end can be changed', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-22T09:30:00Z') });
    const requests = routes({
      'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()),
      'POST /api/backtests': () => json({ errors: [] }, 422),
    });
    renderApp('/strategies/funding-short/run?version=3');

    const end = await screen.findByLabelText('Конец истории');
    expect(end).toHaveValue('2026-11-22');
    await userEvent.clear(end);
    await userEvent.type(end, '2026-06-01');
    await userEvent.click(form().getByRole('button', { name: 'Поставить в очередь' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/backtests')).toHaveLength(1),
    );
    const [body] = (await bodiesOf(requests, 'POST', '/api/backtests')) as { end: string }[];
    expect(body!.end).toBe('2026-06-01');
  });

  // Scenario «Список пар у лесенки».
  it('has no «all pairs» for a strategy of a bot and is not run without a pair', async () => {
    routes({
      'GET /api/strategies/ladder/versions/3': () => json(strategyVersion('bot', 'ladder')),
    });
    renderApp('/strategies/ladder/run?version=3');

    await screen.findByRole('form', { name: 'Запуск прогона' });
    expect(form().queryByRole('radio', { name: 'все пары' })).toBeNull();
    const run = form().getByRole('button', { name: 'Поставить в очередь' });
    expect(run).toBeDisabled();
    expect(run).toHaveAttribute('title', 'добавьте хотя бы одну пару');

    await userEvent.type(form().getByRole('combobox', { name: 'Пара' }), 'btcusdt{Enter}');

    expect(form().getByRole('list', { name: 'Пары прогона' })).toHaveTextContent('BTCUSDT');
    expect(run).toBeEnabled();
  });

  // Task 12.10: the checkboxes of the timeframes are named and valued by their timeframes, not «on».
  it('names the checkboxes of the timeframes by their timeframes', async () => {
    routes({ 'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()) });
    renderApp('/strategies/funding-short/run?version=3');

    const group = within(await screen.findByRole('group', { name: 'Таймфреймы' }));
    const boxes = group.getAllByRole('checkbox');
    expect(boxes.map((box) => box.getAttribute('value'))).toEqual(['5m', '15m', '1h', '4h', '1d']);
    for (const timeframe of ['5m', '15m', '1h', '4h', '1d']) {
      expect(group.getByRole('checkbox', { name: timeframe })).toHaveAttribute('value', timeframe);
    }
  });

  // Task 12.2: a fee written with a comma is read as with a point.
  it('reads a fee written with a comma as with a point', async () => {
    const requests = routes({
      'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()),
      'POST /api/backtests': () => json({ errors: [] }, 422),
    });
    renderApp('/strategies/funding-short/run?version=3');

    const taker = await screen.findByRole('textbox', { name: 'Комиссия тейкера' });
    await userEvent.clear(taker);
    await userEvent.type(taker, '0,0006');
    await userEvent.click(form().getByRole('button', { name: 'Поставить в очередь' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/backtests')).toHaveLength(1),
    );
    const [body] = (await bodiesOf(requests, 'POST', '/api/backtests')) as Record<
      string,
      unknown
    >[];
    expect(body).toMatchObject({ takerFee: 0.0006, makerFee: 0.0002 });
  });

  // Task 12.2: an empty fee or one that is not a number does not go as 0 or null; it is a reason not to run.
  it('is not run with an empty fee or a fee that is not a number', async () => {
    const requests = routes({
      'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()),
      'POST /api/backtests': () => json({ errors: [] }, 422),
    });
    renderApp('/strategies/funding-short/run?version=3');

    const maker = await screen.findByRole('textbox', { name: 'Комиссия мейкера' });
    const run = form().getByRole('button', { name: 'Поставить в очередь' });
    await userEvent.clear(maker);

    expect(run).toBeDisabled();
    expect(run).toHaveAttribute('title', 'укажите комиссию мейкера');

    await userEvent.type(maker, 'abc');

    expect(run).toBeDisabled();
    expect(run).toHaveAttribute('title', 'комиссия мейкера — не число');
    await userEvent.click(run);
    expect(await bodiesOf(requests, 'POST', '/api/backtests')).toHaveLength(0);
  });

  // Task 12.8: the form of a strategy named `new` is not the editor of a new draft with the id `run`.
  it('opens the form of a strategy named new', async () => {
    routes({
      'GET /api/strategies/new/versions/3': () => json(strategyVersion('idea', 'new')),
    });
    renderApp('/strategies/new/run?version=3');

    const form = within(await screen.findByRole('form', { name: 'Запуск прогона' }));
    expect(form.getByRole('heading', { name: 'Прогон new' })).toBeInTheDocument();
    expect(form.getByText('версия 3')).toBeInTheDocument();
  });

  // Scenario «Отказ остаётся в форме».
  it('keeps a refusal and the values in the form', async () => {
    routes({
      'GET /api/strategies/funding-short/versions/3': () => json(strategyVersion()),
      'POST /api/backtests': () =>
        json(
          {
            errors: [
              {
                code: 'ParameterTypeMismatch',
                reason: 'значение abc параметра threshold — не число',
                parameter: 'threshold',
              },
            ],
          },
          422,
        ),
    });
    const { history } = renderApp('/strategies/funding-short/run?version=3');

    const values = await screen.findByRole('textbox', { name: 'Значения threshold' });
    await userEvent.clear(values);
    await userEvent.type(values, 'abc');
    await userEvent.click(form().getByRole('button', { name: 'Поставить в очередь' }));

    const errors = within(await screen.findByRole('list', { name: 'Ошибки' }));
    expect(errors.getByText('параметр threshold')).toBeInTheDocument();
    expect(errors.getByText('значение abc параметра threshold — не число')).toBeInTheDocument();
    expect(values).toHaveValue('abc');
    expect(history.location.pathname).toBe('/strategies/funding-short/run');
  });
});
