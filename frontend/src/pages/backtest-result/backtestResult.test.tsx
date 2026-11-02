import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createSeriesMarkers } from 'lightweight-charts';

import { clearComparison } from '@/modules/comparison';
import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  backtestFull,
  backtestId,
  botDetails,
  botId,
  emaSummary,
  metricsOf,
  resultOf,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const HOUR = 3_600_000;
const start = Date.UTC(2024, 0, 1);

function equityPoints(count: number, step = 1) {
  return Array.from({ length: count }, (_, index) => ({
    at: new Date(start + (index + 1) * step * HOUR).toISOString(),
    equity: '1000',
    minEquity: '990',
    openTime: new Date(start + index * step * HOUR).toISOString(),
    open: '100',
    high: '110',
    low: '95',
    close: '105',
  }));
}

const fillAt = (at: string) => ({
  at,
  price: '43120.5',
  quantity: '0.022',
  fee: '0.5',
  feeAsset: 'quote',
});

const order = (side: 'buy' | 'sell', fills: string[] = [], overrides: object = {}) => ({
  id: `o-${side}`,
  node: 'entry',
  side,
  type: 'market',
  price: null,
  quantity: '0.022',
  amount: null,
  status: 'filled',
  fills: fills.map(fillAt),
  ...overrides,
});

const position = (overrides: object = {}) => ({
  number: 1,
  status: 'closed',
  openedAt: '2024-01-05T14:00:00Z',
  closedAt: '2024-01-11T09:00:00Z',
  carriedQuantity: '0',
  entryPrice: '43120.5',
  exitPrice: '44870',
  pnl: '38.49',
  fees: '1.02',
  orders: [],
  ...overrides,
});

/** One position with an order for each fill. */
const filled = (...fills: [string, 'buy' | 'sell'][]) => [
  position({ orders: fills.map(([at, side], index) => order(side, [at], { id: `o-${index}` })) }),
];

function detailsOf(overrides: object = {}) {
  return {
    metrics: metricsOf(),
    positions: [],
    equity: equityPoints(3),
    ...overrides,
  };
}

function routes(
  details: () => Response,
  direction = 'long',
  extra: Record<string, (request: Request) => Response> = {},
) {
  return {
    [`GET /api/backtests/${backtestId}`]: () =>
      json(backtestFull({ status: 'Succeeded', botChanged: false })),
    [`GET /api/backtests/${backtestId}/results/4`]: () => json(resultOf(4, { return: '18.42' })),
    [`GET /api/backtests/${backtestId}/results/4/details`]: details,
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}/versions/7`]: () =>
      json({
        number: 7,
        createdAt: '2026-11-01T10:00:00Z',
        definition: { direction },
        composites: [],
      }),
    ...extra,
  };
}

const open = () => renderApp(`/backtests/${backtestId}/results/4`);

describe('result details page', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('asks for the result by its number and shows its parameters and metrics', async () => {
    const requests = fakeApi(routes(() => json(detailsOf())));
    open();

    expect(await screen.findByRole('heading', { name: 'Комбинация #4' })).toBeInTheDocument();
    expect(await screen.findByText(/fast 10/)).toHaveTextContent(
      'fast 10 · slow 30 · takeProfit 3.5',
    );
    const paths = requests.map((request) => new URL(request.url).pathname);
    expect(paths).toContain(`/api/backtests/${backtestId}/results/4`);
    expect(paths.filter((path) => path.endsWith('/results'))).toEqual([]);
    const pnl = screen.getByRole('group', { name: 'PnL' });
    expect(pnl).toHaveTextContent('+184.20');
    expect(pnl).toHaveTextContent('реализ. +160.10');
    expect(pnl).toHaveTextContent('нереализ. +24.10');
    expect(screen.getByRole('group', { name: 'Доходность' })).toHaveTextContent('+18.42 %');
    expect(screen.getByRole('group', { name: 'Сделки · прибыльных' })).toHaveTextContent(
      '41 · 58.50 %',
    );
  });

  it('shows the facts of the position', async () => {
    fakeApi(routes(() => json(detailsOf())));
    open();

    const facts = await screen.findByRole('region', { name: 'Показатели позиции' });
    expect(within(facts).getByText('В позиции').parentElement).toHaveTextContent('62.00 %');
    expect(within(facts).getByText('Под водой').parentElement).toHaveTextContent('18.00 %');
    expect(within(facts).getByText('Дольше всего под водой').parentElement).toHaveTextContent(
      '96 свечей',
    );
    expect(within(facts).getByText('Макс. позиция').parentElement).toHaveTextContent(
      '980.00 · 98.00 %',
    );
    expect(within(facts).getByText('Пропущено ордеров').parentElement).toHaveTextContent('3');
    expect(within(facts).getByText('Исполнений').parentElement).toHaveTextContent('82');
  });

  // Scenario "Метки исполнений": one entry and one exit on the chart.
  it('marks the fills on the chart by the direction of the strategy', async () => {
    fakeApi(
      routes(() =>
        json(
          detailsOf({
            positions: filled(['2024-01-05T14:00:00Z', 'buy'], ['2024-01-11T09:00:00Z', 'sell']),
            equity: equityPoints(24 * 12),
          }),
        ),
      ),
    );
    open();

    const chart = await screen.findByRole('figure', { name: 'Цена и капитал' });
    await waitFor(() => expect(chart).toHaveAttribute('data-entries', '1'));
    expect(chart).toHaveAttribute('data-exits', '1');
    expect(chart).toHaveAttribute('data-points', String(24 * 12));
  });

  it('marks a sell as the entry of a short strategy', async () => {
    fakeApi(
      routes(
        () => json(detailsOf({ positions: filled(['2024-01-01T01:00:00Z', 'sell']) })),
        'short',
      ),
    );
    open();

    const chart = await screen.findByRole('figure', { name: 'Цена и капитал' });
    await waitFor(() => expect(chart).toHaveAttribute('data-entries', '1'));
    expect(chart).toHaveAttribute('data-exits', '0');
  });

  it('shows the chart without marks when the version of the strategy is not available', async () => {
    fakeApi({
      ...routes(() => json(detailsOf({ positions: filled(['2024-01-01T01:00:00Z', 'buy']) }))),
      [`GET /api/strategies/${emaSummary.id}/versions/7`]: () => problem(404, 'нет'),
    });
    open();

    const chart = await screen.findByRole('figure', { name: 'Цена и капитал' });
    expect(chart).toHaveAttribute('data-entries', '0');
    expect(chart).toHaveAttribute('data-exits', '0');
  });

  it('names the candles in a point when a point holds one candle', async () => {
    fakeApi(routes(() => json(detailsOf())));
    open();

    expect(await screen.findByText('по 1 свече в точке · 3 точки')).toBeInTheDocument();
  });

  // Scenario "Длинный период".
  it('draws at most 5000 points and names the candles in a point', async () => {
    fakeApi(routes(() => json(detailsOf({ equity: equityPoints(5000, 106) }))));
    open();

    expect(await screen.findByText('по 106 свечей в точке · 5 000 точек')).toBeInTheDocument();
    expect(screen.getByRole('figure', { name: 'Цена и капитал' })).toHaveAttribute(
      'data-points',
      '5000',
    );
  });

  // Scenario "Открытая сделка".
  it('marks the position that is open at the end and leaves its exit and pnl empty', async () => {
    fakeApi(
      routes(() =>
        json(
          detailsOf({
            positions: [
              position(),
              position({
                number: 2,
                status: 'open',
                openedAt: '2024-06-21T16:00:00Z',
                closedAt: null,
                exitPrice: null,
                pnl: '0',
                entryPrice: '47010',
              }),
            ],
          }),
        ),
      ),
    );
    open();

    const closed = (await screen.findByText('2024-01-05 14:00')).closest('tr')!;
    expect(within(closed).getByText('2024-01-11 09:00')).toBeInTheDocument();
    expect(within(closed).getByText('+38.49')).toBeInTheDocument();
    const opened = screen.getByText('2024-06-21 16:00').closest('tr')!;
    const cells = within(opened)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells.slice(0, 7)).toEqual([
      '2',
      'открыта',
      '2024-06-21 16:00',
      '—',
      '47\u00a0010',
      '—',
      '—',
    ]);
  });

  // Scenario "Ордера позиции".
  it('opens a position to its orders and their fills', async () => {
    fakeApi(
      routes(() =>
        json(
          detailsOf({
            positions: [
              position({
                carriedQuantity: '0.000044',
                orders: [
                  order('buy', ['2024-01-05T14:00:00Z']),
                  order('buy', ['2024-01-07T10:00:00Z'], {
                    id: 'o-2',
                    node: 'average',
                    type: 'limit',
                    price: '42000',
                  }),
                  order('sell', ['2024-01-11T09:00:00Z'], { id: 'o-3', node: 'take' }),
                  order('buy', [], {
                    id: 'o-4',
                    node: 'dca',
                    quantity: null,
                    amount: '100',
                    status: 'active',
                  }),
                ],
              }),
            ],
          }),
        ),
      ),
    );
    open();

    const toggle = await screen.findByRole('button', { name: 'Ордера позиции 1' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('перенесено 0.000044')).toBeInTheDocument();
    expect(screen.queryByText('average')).not.toBeInTheDocument();

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table');
    const average = within(table).getByText('average').closest('tr')!;
    expect(
      within(average)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['average', 'Покупка', 'limit', '42\u00a0000', '0.022', 'исполнен']);
    expect(within(table).getByText('entry')).toBeInTheDocument();
    expect(within(table).getByText('take')).toBeInTheDocument();
    expect(within(table).getByText('2024-01-07 10:00')).toBeInTheDocument();
    expect(within(table).getAllByText('0.5 quote')).toHaveLength(3);
    const byAmount = within(table).getByText('dca').closest('tr')!;
    expect(within(byAmount).getAllByRole('cell').slice(4)).toEqual([
      expect.objectContaining({ textContent: '100' }),
      expect.objectContaining({ textContent: 'активен' }),
    ]);
  });

  // Scenario "Продажа позднего исполнения".
  it('names the position as the one that placed its own sell', async () => {
    fakeApi(
      routes(() =>
        json(
          detailsOf({
            positions: [
              position({ orders: [order('sell', ['2024-01-11T09:00:00Z'], { node: null })] }),
            ],
          }),
        ),
      ),
    );
    open();

    await userEvent.click(await screen.findByRole('button', { name: 'Ордера позиции 1' }));

    const sell = screen.getByText('Продажа').closest('tr')!;
    expect(within(sell).getAllByRole('cell')[0]).toHaveTextContent('позиция');
  });

  // Scenario "Движок обновился".
  it('says the details are unavailable and names both engine versions, keeping the metrics', async () => {
    fakeApi(
      routes(() =>
        problem(
          409,
          'The backtest was computed at engine version 2, and the details would be computed at engine version 3.',
        ),
      ),
    );
    open();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Детали недоступны');
    expect(alert).toHaveTextContent('движком v2');
    expect(alert).toHaveTextContent('движком v3');
    expect(screen.getByRole('group', { name: 'Доходность' })).toHaveTextContent('+18.42 %');
    expect(screen.queryByRole('figure')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← К результатам' })).toBeInTheDocument();
  });

  it('shows the answer of the API when the refusal is of another kind', async () => {
    fakeApi(routes(() => problem(409, 'Что-то другое.')));
    open();

    expect(await screen.findByRole('alert')).toHaveTextContent('Что-то другое.');
  });

  // Scenario "Комбинация без результата".
  it('says there is no such result when the backtest has none saved under the number', async () => {
    fakeApi({
      ...routes(() => json(detailsOf())),
      [`GET /api/backtests/${backtestId}/results/4`]: () => problem(404, 'нет'),
    });
    open();

    expect(await screen.findByRole('alert')).toHaveTextContent('Результат не найден.');
  });

  it('says there is no such result when the details are not found', async () => {
    fakeApi(routes(() => problem(404, 'нет')));
    open();

    expect(await screen.findByRole('alert')).toHaveTextContent('Результат не найден.');
    expect(screen.getByRole('group', { name: 'Доходность' })).toHaveTextContent('+18.42 %');
  });

  it('colors the return and the pnl by their sign', async () => {
    const answer = (metrics: object) => ({
      [`GET /api/backtests/${backtestId}/results/4`]: () => json(resultOf(4, metrics)),
    });
    const value = (title: string) =>
      screen.getByRole('group', { name: title }).querySelector('p[title]')!;

    fakeApi({ ...routes(() => json(detailsOf())), ...answer({ return: '18.42', pnl: '184.20' }) });
    const first = open();
    await screen.findByRole('heading', { name: 'Комбинация #4' });
    expect(value('Доходность')).toHaveClass('text-ok');
    expect(value('PnL')).toHaveClass('text-ok');
    first.unmount();

    fakeApi({ ...routes(() => json(detailsOf())), ...answer({ return: '-5.10', pnl: '-51.00' }) });
    const second = open();
    await screen.findByRole('heading', { name: 'Комбинация #4' });
    expect(value('Доходность')).toHaveClass('text-err');
    expect(value('PnL')).toHaveClass('text-err');
    second.unmount();

    fakeApi({ ...routes(() => json(detailsOf())), ...answer({ return: '0', pnl: '0.00' }) });
    open();
    await screen.findByRole('heading', { name: 'Комбинация #4' });
    for (const title of ['Доходность', 'PnL']) {
      expect(value(title)).not.toHaveClass('text-ok');
      expect(value(title)).not.toHaveClass('text-err');
    }
  });

  it('colors the pnl of a position by its sign', async () => {
    fakeApi(
      routes(() =>
        json(
          detailsOf({
            positions: [
              position({ number: 1, openedAt: '2024-01-01T00:00:00Z', pnl: '38.49' }),
              position({ number: 2, openedAt: '2024-01-02T00:00:00Z', pnl: '-12.00' }),
              position({ number: 3, openedAt: '2024-01-03T00:00:00Z', pnl: '0' }),
              position({
                number: 4,
                status: 'open',
                openedAt: '2024-01-04T00:00:00Z',
                closedAt: null,
                pnl: '-3',
              }),
            ],
          }),
        ),
      ),
    );
    open();

    const pnl = async (opened: string) =>
      within((await screen.findByText(opened)).closest('tr')!).getAllByRole('cell')[6]!;
    expect(await pnl('2024-01-01 00:00')).toHaveClass('text-ok');
    expect(await pnl('2024-01-02 00:00')).toHaveClass('text-err');
    expect(await pnl('2024-01-03 00:00')).not.toHaveClass('text-ok', 'text-err');
    expect(await pnl('2024-01-04 00:00')).not.toHaveClass('text-ok', 'text-err');
  });

  it('keeps the marks on the chart when the page is drawn again without new fills', async () => {
    fakeApi(routes(() => json(detailsOf({ positions: filled(['2024-01-01T01:00:00Z', 'buy']) }))));
    open();
    await screen.findByRole('figure', { name: 'Цена и капитал' });
    const plugin = vi.mocked(createSeriesMarkers).mock.results.at(-1)!.value as {
      setMarkers: ReturnType<typeof vi.fn>;
    };
    await waitFor(() => expect(plugin.setMarkers).toHaveBeenLastCalledWith([expect.anything()]));
    const drawn = plugin.setMarkers.mock.calls.length;

    await userEvent.click(screen.getByRole('button', { name: 'Сравнить' }));

    expect(screen.getByRole('button', { name: '✓ В сравнении' })).toBeInTheDocument();
    expect(plugin.setMarkers).toHaveBeenCalledTimes(drawn);
  });

  it('marks the result for comparison and takes the mark off', async () => {
    fakeApi(routes(() => json(detailsOf())));
    open();

    const button = await screen.findByRole('button', { name: 'Сравнить' });
    expect(button).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(button);

    expect(screen.getByRole('button', { name: '✓ В сравнении' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('region', { name: 'Сравнение' })).toHaveTextContent('1 из 6');
    await userEvent.click(screen.getByRole('button', { name: '✓ В сравнении' }));
    expect(screen.queryByRole('region', { name: 'Сравнение' })).not.toBeInTheDocument();
  });
});
