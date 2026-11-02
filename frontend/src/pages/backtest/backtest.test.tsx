import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { clearComparison, toggleComparison } from '@/modules/comparison';
import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  backtestFull,
  backtestId,
  botDetails,
  botId,
  emaDefinition,
  emaSummary,
  otherBacktestId,
  progressOf,
  resultOf,
  savedStrategy,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Row = ReturnType<typeof resultOf>;

/** Answers like the server: sorts all rows by the metric, then cuts the page. */
function resultsServer(rows: () => Row[]) {
  return (request: Request) => {
    const query = new URL(request.url).searchParams;
    const sort = query.get('Sort') ?? 'combination';
    const sign = query.get('Order') === 'desc' ? -1 : 1;
    const value = (row: Row) =>
      sort === 'combination'
        ? row.combination
        : Number((row.metrics as unknown as Record<string, string>)[sort]);
    const all = [...rows()].sort((a, b) => sign * (value(a) - value(b)));
    const offset = Number(query.get('Offset') ?? 0);
    const limit = Number(query.get('Limit') ?? 100);

    return json({ total: all.length, items: all.slice(offset, offset + limit) });
  };
}

function routes(
  backtest: () => unknown,
  rows: () => Row[] = () => [],
  extra: Record<string, (request: Request) => Response> = {},
) {
  return {
    [`GET /api/backtests/${backtestId}`]: () => json(backtest()),
    [`GET /api/backtests/${backtestId}/results`]: resultsServer(rows),
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, emaDefinition)),
    ...extra,
  };
}

const resultRequests = (requests: Request[]) =>
  requests.filter((request) => new URL(request.url).pathname.endsWith('/results'));
const lastResultQuery = (requests: Request[]) =>
  new URL(resultRequests(requests).at(-1)!.url).searchParams;

const grid = (count: number) =>
  Array.from({ length: count }, (_, index) =>
    resultOf(index, { return: String(index), maxDrawdown: String(100 - index) }),
  );

describe('backtest page', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows the bot, the strategy version, the instrument, the period and the engine', async () => {
    fakeApi(routes(() => backtestFull()));
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByRole('heading', { name: 'Бэктест' })).toBeInTheDocument();
    expect(
      await screen.findByText(
        'EMA v7 · linear · BTCUSDT · 1 ч · 2024-01-01 – 2024-07-01 UTC · 9 комбинаций · движок v3',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'BTC EMA час' })).toBeInTheDocument();
  });

  // Scenario "Расчёт идёт".
  it('shows the combinations done and the speed, and turns ready without a reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let status = 'Running';
    let calculated = 5;
    const requests = fakeApi(
      routes(
        () =>
          backtestFull({
            status,
            progress: progressOf(calculated, 9),
            speed: status === 'Running' ? '412000' : null,
          }),
        () => grid(calculated),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('5 из 9 комбинаций')).toBeInTheDocument();
    expect(screen.getByText('412 000 свечей/с')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '55');
    expect(screen.getByText('Расчёт')).toHaveAttribute('aria-current', 'step');
    expect(await screen.findByText('посчитано 5 из 9 — таблица дополняется')).toBeInTheDocument();

    status = 'Succeeded';
    calculated = 9;
    await act(() => vi.advanceTimersByTimeAsync(2000));

    expect(await screen.findByText('Готов')).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(10));
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();

    const before = requests.length;
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(requests.length).toBe(before);
  });

  it('shows no speed while the speed is not known', async () => {
    fakeApi(
      routes(() => backtestFull({ status: 'Running', progress: progressOf(5, 9), speed: null })),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('5 из 9 комбинаций')).toBeInTheDocument();
    expect(screen.queryByText(/свечей\/с/)).not.toBeInTheDocument();
  });

  it('shows the candles loaded while the candles load', async () => {
    fakeApi(
      routes(() =>
        backtestFull({
          status: 'LoadingCandles',
          speed: null,
          progress: { ...progressOf(0, 9), loadedCandles: 3120, expectedCandles: 4344 },
        }),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('Свечи: 3 120 из 4 344 загружены')).toBeInTheDocument();
    expect(screen.getByText('Загрузка свечей')).toHaveAttribute('aria-current', 'step');
  });

  it('waits in the queue without progress and has no results yet', async () => {
    fakeApi(
      routes(() => backtestFull({ status: 'Pending', speed: null, progress: progressOf(0, 9) })),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('Ожидание')).toHaveAttribute('aria-current', 'step');
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.queryByText(/Свечи:/)).not.toBeInTheDocument();
    expect(await screen.findByText('Результатов пока нет.')).toBeInTheDocument();
  });

  it('says when the backtest cannot be loaded', async () => {
    fakeApi({ [`GET /api/backtests/${backtestId}`]: () => problem(404, 'нет') });
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бэктест.');
  });

  it('shows the reason a backtest failed', async () => {
    fakeApi(routes(() => backtestFull({ status: 'Failed', error: 'Bybit: биржа недоступна' })));
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Bybit: биржа недоступна');
    expect(screen.getByText('Ошибка')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Отменить' })).not.toBeInTheDocument();
  });

  // Scenario "Период до листинга".
  it('says when the first candle is later than the start of the period', async () => {
    fakeApi(
      routes(() =>
        backtestFull({
          status: 'Succeeded',
          from: '2024-01-01T00:00:00Z',
          to: '2024-03-01T00:00:00Z',
          actualStart: '2024-02-01T00:00:00Z',
        }),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText(/Первая свеча — 2024-02-01 00:00/)).toBeInTheDocument();
  });

  it('lists the gaps between candles and says nothing when the candles are whole', async () => {
    fakeApi(
      routes(() =>
        backtestFull({
          status: 'Succeeded',
          gaps: [{ from: '2024-01-05T03:00:00Z', to: '2024-01-05T07:00:00Z' }],
        }),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(
      await screen.findByText(/Пропуски в свечах: 2024-01-05 03:00 – 2024-01-05 07:00/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Первая свеча/)).not.toBeInTheDocument();
  });

  // Scenario "Новая версия стратегии".
  it('warns that the bot changed and names the version of the backtest', async () => {
    fakeApi(routes(() => backtestFull({ status: 'Succeeded', botChanged: true })));
    renderApp(`/backtests/${backtestId}`);

    const warning = await screen.findByRole('status');
    expect(warning).toHaveTextContent('Бот изменился');
    expect(warning).toHaveTextContent('v7');
  });

  it('has no warning while the bot is as it was', async () => {
    fakeApi(routes(() => backtestFull({ status: 'Succeeded' })));
    renderApp(`/backtests/${backtestId}`);

    await screen.findByText('Готов');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  // Scenario "Отмена во время расчёта".
  it('cancels after the confirmation and keeps the results calculated so far', async () => {
    let cancelled = false;
    const requests = fakeApi(
      routes(
        () =>
          backtestFull(
            cancelled
              ? { status: 'Cancelled', progress: progressOf(7, 19), speed: null }
              : { status: 'Running', progress: progressOf(7, 19) },
          ),
        () => grid(7),
        {
          [`POST /api/backtests/${backtestId}/cancel`]: () => {
            cancelled = true;

            return new Response(null, { status: 204 });
          },
        },
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Отменить' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Посчитанные результаты сохранятся.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Отменить бэктест' }));

    expect(await screen.findByText('Отменён · 7 / 19')).toBeInTheDocument();
    expect(requests.some((request) => request.url.endsWith('/cancel'))).toBe(true);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(8));
  });

  it('does not cancel when the confirmation is declined', async () => {
    const requests = fakeApi(routes(() => backtestFull()));
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Отменить' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Не отменять' }),
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  it('shows why the cancellation was refused', async () => {
    fakeApi(
      routes(
        () => backtestFull(),
        () => [],
        {
          [`POST /api/backtests/${backtestId}/cancel`]: () => problem(409, 'Бэктест уже завершён.'),
        },
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Отменить' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Отменить бэктест' }),
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('Бэктест уже завершён.');
  });
});

describe('results table', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Колонки параметров".
  it('has columns for the parameters given as ranges and none for the others', async () => {
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(9),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    const table = await screen.findByRole('table');
    await within(table).findByText('+8.00 %');
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
    expect(headers).toContain('fast');
    expect(headers).toContain('slow');
    expect(headers).not.toContain('takeProfit');
  });

  it('shows the number, the values of the ranges and the metrics of a result', async () => {
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => [resultOf(4, { pnl: '1184.20', returnToDrawdown: null, winRate: '58.5' })],
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    const row = (await screen.findByRole('link', { name: '4' })).closest('tr')!;
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells).toEqual([
      '',
      '4',
      '10',
      '30',
      '+18.42 %',
      '6.10 %',
      '—',
      '41',
      '58.50 %',
      '21.70',
      '+1\u00a0184.20',
    ]);
  });

  it('sorts by return, best first, to begin with', async () => {
    const requests = fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(9),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await screen.findByRole('table');
    await waitFor(() => expect(resultRequests(requests)).not.toHaveLength(0));
    const query = lastResultQuery(requests);
    expect([
      query.get('Sort'),
      query.get('Order'),
      query.get('Offset'),
      query.get('Limit'),
    ]).toEqual(['return', 'desc', '0', '100']);
    expect(screen.getByRole('columnheader', { name: /Доходность/ })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });

  // Scenario "Лучшие по просадке".
  it('sorts on the server by the drawdown, lowest first, over all the results', async () => {
    const requests = fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(240),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Макс. просадка' }));

    await waitFor(() => expect(lastResultQuery(requests).get('Sort')).toBe('maxDrawdown'));
    expect(lastResultQuery(requests).get('Order')).toBe('asc');
    const first = (await screen.findByText('1 – 100 из 240'))
      .closest('section')!
      .querySelectorAll('tbody tr')[0]!;
    expect(first).toHaveTextContent('239');
    expect(first).toHaveTextContent('-139.00 %');
  });

  it('flips the order on a second click on the same column', async () => {
    const requests = fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(9),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Доходность' }));

    await waitFor(() => expect(lastResultQuery(requests).get('Order')).toBe('asc'));
    expect(lastResultQuery(requests).get('Sort')).toBe('return');
  });

  it('sorts by the number of the combination', async () => {
    const requests = fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(9),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: '#' }));

    await waitFor(() => expect(lastResultQuery(requests).get('Sort')).toBe('combination'));
    expect(lastResultQuery(requests).get('Order')).toBe('asc');
  });

  it('shows the results by pages of 100 and goes back to the first page when the sort changes', async () => {
    const requests = fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(240),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('1 – 100 из 240')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '← Назад' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Вперёд →' }));
    expect(await screen.findByText('101 – 200 из 240')).toBeInTheDocument();
    expect(lastResultQuery(requests).get('Offset')).toBe('100');
    await userEvent.click(screen.getByRole('button', { name: 'Вперёд →' }));
    expect(await screen.findByText('201 – 240 из 240')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Вперёд →' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Макс. просадка' }));
    expect(await screen.findByText('1 – 100 из 240')).toBeInTheDocument();
  });

  it('goes back a page', async () => {
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => grid(240),
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Вперёд →' }));
    expect(await screen.findByText('101 – 200 из 240')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: '← Назад' }));

    expect(await screen.findByText('1 – 100 из 240')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '← Назад' })).toBeDisabled();
  });

  it('colors the return and the pnl by their sign', async () => {
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => [
          resultOf(1, { return: '4.25', pnl: '42.50' }),
          resultOf(2, { return: '-3.5', pnl: '-35' }),
          resultOf(3, { return: '0', pnl: '0' }),
        ],
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    const cells = async (number: string) =>
      within((await screen.findByRole('link', { name: number })).closest('tr')!).getAllByRole(
        'cell',
      );
    const [gainReturn, gainPnl] = [(await cells('1'))[4]!, (await cells('1')).at(-1)!];
    expect([gainReturn, gainPnl].map((cell) => cell.textContent)).toEqual(['+4.25 %', '+42.50']);
    expect(gainReturn).toHaveClass('text-ok');
    expect(gainPnl).toHaveClass('text-ok');
    expect((await cells('2'))[4]).toHaveClass('text-err');
    expect((await cells('2')).at(-1)).toHaveClass('text-err');
    expect((await cells('3'))[4]).not.toHaveClass('text-ok', 'text-err');
    expect((await cells('3')).at(-1)).not.toHaveClass('text-ok', 'text-err');
  });

  it('says when the results cannot be loaded', async () => {
    fakeApi({
      ...routes(() => backtestFull({ status: 'Succeeded' })),
      [`GET /api/backtests/${backtestId}/results`]: () => problem(404, 'нет'),
    });
    renderApp(`/backtests/${backtestId}`);

    expect(await screen.findByText('Не удалось загрузить результаты.')).toBeInTheDocument();
  });

  it('opens the details of the result of a row', async () => {
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => [resultOf(4)],
      ),
    );
    const { history } = renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByText('+18.42 %'));

    await waitFor(() =>
      expect(history.location.pathname).toBe(`/backtests/${backtestId}/results/4`),
    );
  });

  // Scenario "Результаты разных бэктестов".
  it('marks results of different backtests and shows the count on the bar', async () => {
    toggleComparison({ backtestId: otherBacktestId, combination: 212 });
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => [resultOf(4)],
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    const bar = await screen.findByRole('region', { name: 'Сравнение' });
    expect(bar).toHaveTextContent('1 из 6');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Сравнить результат 4' }));

    expect(bar).toHaveTextContent('2 из 6');
    expect(screen.getByRole('checkbox', { name: 'Сравнить результат 4' })).toBeChecked();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Сравнить результат 4' }));
    expect(bar).toHaveTextContent('1 из 6');
  });

  // Scenario "Седьмой результат".
  it('does not add a seventh result and says the set holds six at most', async () => {
    for (let combination = 100; combination < 106; combination++) {
      toggleComparison({ backtestId: otherBacktestId, combination });
    }
    fakeApi(
      routes(
        () => backtestFull({ status: 'Succeeded' }),
        () => [resultOf(4)],
      ),
    );
    renderApp(`/backtests/${backtestId}`);

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Сравнить результат 4' }));

    expect(screen.getByRole('checkbox', { name: 'Сравнить результат 4' })).not.toBeChecked();
    expect(screen.getByRole('region', { name: 'Сравнение' })).toHaveTextContent('6 из 6');
    expect(await screen.findByText('В наборе не больше шести результатов.')).toBeInTheDocument();
  });
});

describe('comparison bar', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is not on the page while the set is empty', async () => {
    fakeApi(routes(() => backtestFull({ status: 'Succeeded' })));
    renderApp(`/backtests/${backtestId}`);

    await screen.findByText('Готов');
    expect(screen.queryByRole('region', { name: 'Сравнение' })).not.toBeInTheDocument();
  });

  it('links to the comparison of the set and clears it', async () => {
    toggleComparison({ backtestId, combination: 4 });
    toggleComparison({ backtestId: otherBacktestId, combination: 212 });
    fakeApi(routes(() => backtestFull({ status: 'Succeeded' })));
    renderApp(`/backtests/${backtestId}`);

    const link = await screen.findByRole('link', { name: 'Сравнить →' });
    expect(link).toHaveAttribute(
      'href',
      `/compare?items=${backtestId}%3A4%2C${otherBacktestId}%3A212`,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Очистить' }));

    expect(screen.queryByRole('region', { name: 'Сравнение' })).not.toBeInTheDocument();
  });
});
