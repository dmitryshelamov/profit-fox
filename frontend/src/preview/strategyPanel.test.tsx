import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { clickChart, drawn } from '@/test/chart';
import { findNode, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { savedFundingShort, strategyRoutes } from '@/test/strategies';

import { chartColors } from './CandleChart';

const hour = 3_600_000;
const hour4 = 4 * hour;
const first = Date.UTC(2026, 3, 16, 0); // a candle 2026-04-16 00:00 UTC, closed 04:00

interface OldOrder {
  readonly moment: number;
  readonly side: string;
  readonly outcome: string;
  readonly reason: string | null;
  readonly trade: {
    readonly entryTime: number;
    readonly entryPrice: number;
    readonly exitTime: number;
    readonly exitPrice: number;
    readonly exitReason: string;
    readonly net: number;
  } | null;
}

/**
 * The orders as the tests write them — each with the trade of its plan — in the answer of change add-position-node: an
 * order of `enter` of 100 USDT that opened a cycle of an entry and an exit, the others without a cycle.
 */
function withCycles(orders: readonly OldOrder[]) {
  const cycles: unknown[] = [];
  const answered = orders.map((order) => {
    if (order.trade === null) {
      return {
        moment: order.moment,
        node: 'enter',
        side: order.side,
        outcome: order.outcome,
        reason: order.reason,
        price: null,
        amount: null,
        cycle: null,
      };
    }

    const { trade } = order;
    const quantity = 100 / trade.entryPrice;
    cycles.push({
      side: order.side,
      fills: [
        {
          moment: trade.entryTime,
          action: 'entry',
          side: order.side === 'short' ? 'sell' : 'buy',
          price: trade.entryPrice,
          quantity,
          amount: 100,
          node: 'enter',
        },
        {
          moment: trade.exitTime,
          action: 'exit',
          side: order.side === 'short' ? 'buy' : 'sell',
          price: trade.exitPrice,
          quantity,
          amount: quantity * trade.exitPrice,
          node: null,
        },
      ],
      entries: 1,
      invested: 100,
      exitReason: trade.exitReason,
      ambiguous: false,
      net: trade.net,
      netAmount: trade.net * 100,
      open: false,
      exclusion: null,
    });
    return {
      moment: order.moment,
      node: 'enter',
      side: order.side,
      outcome: 'filled',
      reason: null,
      price: trade.entryPrice,
      amount: 100,
      cycle: cycles.length - 1,
    };
  });
  return { orders: answered, cycles };
}

/** A preview of funding-short: a trade on the first event, a skipped order an hour later and one past the end. */
function strategyPreview() {
  return {
    instrument: 'PRLUSDT',
    timeframe: '4h',
    candles: [0, 1, 2, 3].map((index) => ({
      openTime: first + index * hour4,
      open: 1,
      high: 2,
      low: 0.5,
      close: 1.5,
      volume: 10,
    })),
    coverage: [{ from: Date.UTC(2023, 10, 18), to: Date.UTC(2026, 4, 16) }],
    ...withCycles([
      {
        moment: first + hour4,
        side: 'short',
        outcome: 'opened',
        reason: null,
        trade: {
          entryTime: first + hour4,
          entryPrice: 1.5,
          exitTime: first + 2 * hour4,
          exitPrice: 1.5,
          exitReason: 'time',
          net: -0.0021,
        },
      },
      {
        moment: first + 2 * hour4 - hour4 / 2,
        side: 'short',
        outcome: 'skipped',
        reason: null,
        trade: null,
      },
      {
        moment: first + 3 * hour4,
        side: 'short',
        outcome: 'excluded',
        reason: 'period',
        trade: null,
      },
    ]),
    periodEnd: Date.UTC(2026, 4, 16),
    computedFrom: Date.UTC(2026, 3, 13),
  };
}

function openFundingShort(preview: object = strategyPreview()) {
  const requests = fakeApi({
    ...baseRoutes(),
    ...strategyRoutes(),
    'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
    'POST /api/preview/strategy': () => json(preview),
  });
  renderApp('/strategies/funding-short');
  return requests;
}

async function show() {
  await userEvent.type(screen.getByRole('combobox', { name: 'Пара' }), 'prlusdt');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Показать' })).toBeEnabled(), {
    timeout: 2000,
  });
  await userEvent.click(screen.getByRole('button', { name: 'Показать' }));
}

/** The list of orders: the tab «Заявки» of the result, which opens on «Сделки». */
async function ordersTable() {
  await userEvent.click(await screen.findByRole('tab', { name: /^Заявки/ }));
  return screen.getByRole('table', { name: 'Заявки' });
}

describe('the preview panel of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Сделки на графике».
  it('marks the entry and the exit of a trade and lists the order with its side, exit and result', async () => {
    const requests = openFundingShort();
    await findNode('funding-high', 's');

    await show();

    const orders = await ordersTable();
    const opened = within(orders).getByRole('row', { name: 'Заявка 2026-04-16 07:00' });
    expect(within(opened).getByText('шорт')).toBeInTheDocument();
    expect(within(opened).getByText('срок, -0.21 %')).toBeInTheDocument();
    expect(drawn.markers).toEqual([
      expect.objectContaining({ time: first / 1000, shape: 'arrowDown', text: 'шорт' }),
      expect.objectContaining({ time: (first + hour4) / 1000, shape: 'circle', text: 'выход' }),
    ]);
    const panel = within(screen.getByRole('region', { name: 'Предпросмотр' }));
    expect(panel.getByRole('status', { name: 'Результат' })).toHaveTextContent('Заявок 3');
    expect(panel.getByRole('status', { name: 'Результат' })).toHaveTextContent('Сделок 1');
    expect(panel.getByText(/Сделки посчитаны с 2026-04-13/)).toBeInTheDocument();
    const [body] = (await bodiesOf(requests, 'POST', '/api/preview/strategy')) as {
      instrument: string;
      strategy: { nodes: { kind: string }[] };
    }[];
    expect(body!.instrument).toBe('PRLUSDT');
    expect(body!.strategy.nodes.map((node) => node.kind)).toEqual([
      'signal',
      'enter',
      'close-after',
    ]);

    await userEvent.click(within(opened).getByRole('button', { name: '2026-04-16 07:00' }));
    expect(drawn.range!.from).toBeLessThan(first / 1000);
    expect(drawn.range!.to).toBeGreaterThan(first / 1000);
  });

  // Scenario «Пропущенная заявка в списке».
  it('marks a skipped order as skipped, without marks on the chart', async () => {
    openFundingShort();
    await findNode('funding-high', 's');

    await show();

    const orders = await ordersTable();
    expect(within(orders).getByRole('row', { name: 'Заявка 2026-04-16 09:00' })).toHaveTextContent(
      'пропущена: позиция открыта',
    );
    expect(within(orders).getByRole('row', { name: 'Заявка 2026-04-16 15:00' })).toHaveTextContent(
      'без сделки: срок за концом разведки',
    );
    expect(drawn.markers).toHaveLength(2);
  });

  // Scenario «Устаревший предпросмотр».
  it('says the graph changed after the time of close-after changed and keeps the old marks', async () => {
    openFundingShort();
    await findNode('funding-high', 's');
    await show();
    await ordersTable();
    expect(screen.queryByText('Граф изменён, нажмите Показать')).toBeNull();

    const closeAfter = await selectNode('Закрыть через', 'close-after');
    const after = within(closeAfter).getByRole('textbox', { name: 'Значение after' });
    await userEvent.clear(after);
    await userEvent.type(after, '8h');
    fireEvent.blur(after);

    expect(await screen.findByText('Граф изменён, нажмите Показать')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Заявки' })).toBeInTheDocument();
    expect(drawn.markers).toHaveLength(2);
  });
});

// Change add-position-node, task 12.6: the marks of the last weeks ran into a blot on the chart of the whole history.
describe('the part of the chart a preview of a strategy opens on', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('is the weeks the trades are computed for, with a little before them', async () => {
    const preview = strategyPreview();
    openFundingShort(asWritten(preview));
    await findNode('funding-high', 's');
    drawn.range = null;

    await show();
    await screen.findByRole('tablist', { name: 'Результат' });

    const from = preview.computedFrom / 1000;
    expect(drawn.view).toBe('range');
    expect(drawn.range!.to).toBe(preview.periodEnd / 1000);
    expect(drawn.range!.from).toBeLessThan(from);
    expect(drawn.range!.from).toBeGreaterThan(from - (3 * 24 * hour) / 1000);
    // The whole history is still on the chart, to scroll back to.
    expect(drawn.candles).toHaveLength(4);
  });

  it('is the whole history when the trades are computed for all of it', async () => {
    openFundingShort(asWritten({ ...strategyPreview(), computedFrom: null }));
    await findNode('funding-high', 's');
    drawn.range = null;

    await show();
    await screen.findByRole('tablist', { name: 'Результат' });

    expect(drawn.candles).toHaveLength(4);
    expect(drawn.view).toBe('content');
    expect(drawn.range).toBeNull();
  });
});

const day = 24 * hour;
const october = Date.UTC(2025, 9, 5, 8); // an order at 2025-10-05 08:00 UTC

/** An order of a short at `moment`; `net` opens a trade that ends `held` later with that result after costs. */
function order(moment: number, net: number | null, outcome = 'opened', held = hour4): OldOrder {
  return {
    moment,
    side: 'short',
    outcome: net === null ? outcome : 'filled',
    reason: null as string | null,
    trade:
      net === null
        ? null
        : {
            entryTime: moment,
            entryPrice: 1.5,
            exitTime: moment + held,
            exitPrice: 1.5,
            exitReason: 'time',
            net,
          },
  };
}

/** Opens `funding-short` and previews it with these orders on candles of 4h from 2025-10-05 00:00. */
async function previewOrders(orders: OldOrder[], answer: object = withCycles(orders)) {
  const requests = fakeApi({
    ...baseRoutes(),
    ...strategyRoutes(),
    'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
    'POST /api/preview/strategy': () =>
      json({
        ...strategyPreview(),
        candles: Array.from({ length: 12 }, (_, index) => ({
          openTime: october - 2 * hour4 + index * hour4,
          open: 1,
          high: 2,
          low: 0.5,
          close: 1.5,
          volume: 10,
        })),
        ...answer,
        computedFrom: null,
      }),
  });
  renderApp('/strategies/funding-short');
  await findNode('funding-high', 's');
  await show();
  await screen.findByRole('tablist', { name: 'Результат' });
  return requests;
}

const selectedRows = (table: HTMLElement) =>
  within(table)
    .getAllByRole('row')
    .filter((row) => row.getAttribute('aria-selected') === 'true')
    .map((row) => row.getAttribute('aria-label'));

// Spec strategy-constructor «Вкладки предпросмотра стратегии».
describe('the tabs of the preview of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Сделки и пропущенная заявка».
  it('opens on «Сделки» with the trades, and lists every order with its outcome on «Заявки»', async () => {
    const requests = await previewOrders([
      order(october, -0.0098),
      order(october + hour, null, 'skipped'),
      order(october + day, 0.0123, 'opened', 2 * hour4 + 30 * 60_000),
    ]);

    const trades = screen.getByRole('tab', { name: 'Сделки 2' });
    const orders = screen.getByRole('tab', { name: 'Заявки 3' });
    expect(trades).toHaveAttribute('aria-selected', 'true');
    const table = screen.getByRole('table', { name: 'Сделки' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Вход, UTC+3', 'Сторона', 'Вх.', 'Вложено', 'Выход', 'Результат']);
    const rows = within(table).getAllByRole('row', { name: /^Сделка / });
    expect(rows.map((row) => row.getAttribute('aria-label'))).toEqual([
      'Сделка 2025-10-05 11:00',
      'Сделка 2025-10-06 11:00',
    ]);
    expect(within(rows[0]!).getByText('шорт')).toHaveAttribute('data-tone', 'err');
    expect(rows[0]).toHaveTextContent('по сроку · 4h');
    expect(within(rows[0]!).getByText('-0.98 %').parentElement).toHaveAttribute('data-tone', 'err');
    expect(rows[0]).toHaveTextContent('-0.98 USDT');
    expect(rows[1]).toHaveTextContent('по сроку · 8h 30m');
    expect(within(rows[1]!).getByText('+1.23 %').parentElement).toHaveAttribute('data-tone', 'ok');
    expect(screen.queryByRole('table', { name: 'Заявки' })).toBeNull();

    await userEvent.click(orders);

    expect(orders).toHaveAttribute('aria-selected', 'true');
    const list = screen.getByRole('table', { name: 'Заявки' });
    expect(within(list).getAllByRole('row', { name: /^Заявка / })).toHaveLength(3);
    // Change fix-pair-chart-and-local-time, task 3.3: the column of the moments names the zone of the browser.
    expect(within(list).getAllByRole('columnheader')[0]).toHaveTextContent('Момент, UTC+3');
    expect(within(list).getByRole('row', { name: 'Заявка 2025-10-05 12:00' })).toHaveTextContent(
      'пропущена: позиция открыта',
    );
    // The choice of a tab does not run the preview again.
    expect(await bodiesOf(requests, 'POST', '/api/preview/strategy')).toHaveLength(1);
  });

  // Change add-position-node, task 12.4: the six columns fit the 470 px of the right part of the panel. jsdom lays
  // nothing out, so this holds what the width rests on; the width itself is measured in a browser.
  it('keeps the table of trades narrow: tight cells, the moment on one line, a short head of the entries', async () => {
    await previewOrders([order(october, -0.0098)]);

    const table = screen.getByRole('table', { name: 'Сделки' });
    const row = within(table).getByRole('row', { name: 'Сделка 2025-10-05 11:00' });
    const cells = within(row).getAllByRole('cell');
    for (const cell of [...within(table).getAllByRole('columnheader'), ...cells]) {
      expect(cell).toHaveClass('px-1');
      expect(cell).not.toHaveClass('px-4');
    }

    expect(cells[0]).toHaveClass('whitespace-nowrap', 'text-xs');
    expect(within(table).getByRole('columnheader', { name: 'Вх.' })).toHaveAttribute(
      'title',
      'Входов',
    );
    // Only the exit gives way to the others: it breaks before the time the cycle was held, which stays whole.
    expect(cells[4]).toHaveTextContent('по сроку · 4h');
    expect(within(cells[4]!).getByText('4h')).toHaveClass('whitespace-nowrap');
    expect(within(cells[5]!).getByText('-0.98 %').parentElement).toHaveClass('whitespace-nowrap');
  });

  // Scenario «Сделок нет».
  it('says that there are no trades, and still lists the orders with their reasons', async () => {
    await previewOrders([
      order(october, null, 'skipped'),
      { ...order(october + day, null, 'excluded'), reason: 'coverage' },
    ]);

    expect(screen.getByRole('tab', { name: 'Сделки 0' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Сделок нет.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Заявки 2' }));

    const list = screen.getByRole('table', { name: 'Заявки' });
    expect(within(list).getByRole('row', { name: 'Заявка 2025-10-05 11:00' })).toHaveTextContent(
      'пропущена: позиция открыта',
    );
    expect(within(list).getByRole('row', { name: 'Заявка 2025-10-06 11:00' })).toHaveTextContent(
      'без сделки: нет минутной истории',
    );
  });

  // Scenario «Цикл, исключённый дырой в истории».
  it('says that there is no minute history at the order whose cycle a hole in the history left out', async () => {
    const answer = withCycles([order(october, -0.0098)]);
    const [cycle] = answer.cycles as { fills: unknown[] }[];
    // The cycle as the API writes it: its entry, no exit and no result.
    await previewOrders(
      [],
      asWritten({
        ...answer,
        cycles: [
          {
            ...cycle,
            fills: cycle!.fills.slice(0, 1),
            exitReason: null,
            net: null,
            netAmount: null,
            exclusion: 'coverage',
          },
        ],
      }),
    );

    expect(screen.getByRole('tab', { name: 'Сделки 0' })).toHaveAttribute('aria-selected', 'true');

    await userEvent.click(screen.getByRole('tab', { name: 'Заявки 1' }));

    const row = within(screen.getByRole('table', { name: 'Заявки' })).getByRole('row', {
      name: 'Заявка 2025-10-05 11:00',
    });
    expect(row).toHaveTextContent('исполнена, нет минутной истории');
    expect(row).not.toHaveTextContent('цикл открыт');
  });
});

// Spec strategy-constructor «Сумма результатов предпросмотра стратегии».
describe('the numbers of the preview of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  const result = () =>
    within(screen.getByRole('region', { name: 'Предпросмотр' })).getByRole('status', {
      name: 'Результат',
    });

  // Scenario «Две убыточные сделки».
  it('shows the orders, the trades and the sum of the results in the color of a loss', async () => {
    await previewOrders([order(october, -0.0098), order(october + day, -0.0056)]);

    expect(result()).toHaveTextContent('Заявок 2');
    expect(result()).toHaveTextContent('Сделок 2');
    expect(result()).toHaveTextContent('Итог -1.54 % · -1.54 USDT');
    expect(within(result()).getByText('-1.54 %')).toHaveAttribute('data-tone', 'err');
    expect(within(result()).getByText('-1.54 USDT')).toHaveAttribute('data-tone', 'err');
  });

  it('shows a positive sum with its sign in the color of a profit', async () => {
    await previewOrders([order(october, -0.0098), order(october + day, 0.02)]);

    expect(result()).toHaveTextContent('Итог +1.02 % · +1.02 USDT');
    expect(within(result()).getByText('+1.02 %')).toHaveAttribute('data-tone', 'ok');
    expect(within(result()).getByText('+1.02 USDT')).toHaveAttribute('data-tone', 'ok');
  });

  // Change add-position-node, task 12.7: the sum of the fractions and the sum of USDT of cycles of different sizes
  // can have different signs.
  it('colors the percent and the USDT of the sum each by its own sign', async () => {
    const answer = withCycles([order(october, 0.02), order(october + day, -0.01)]);
    const [small, large] = answer.cycles as object[];
    await previewOrders(
      [],
      asWritten({
        ...answer,
        // +2 % of 100 USDT and -1 % of 1000 USDT: +1 % in all, and a loss of 8 USDT.
        cycles: [small, { ...large, invested: 1000, netAmount: -10 }],
      }),
    );

    expect(result()).toHaveTextContent('Итог +1.00 % · -8.00 USDT');
    expect(within(result()).getByText('+1.00 %')).toHaveAttribute('data-tone', 'ok');
    expect(within(result()).getByText('-8.00 USDT')).toHaveAttribute('data-tone', 'err');
  });

  // Scenario «Без сделок».
  it('shows no sum without trades', async () => {
    await previewOrders([order(october, null, 'skipped')]);

    expect(result()).toHaveTextContent('Заявок 1');
    expect(result()).toHaveTextContent('Сделок 0');
    expect(result()).not.toHaveTextContent('Итог');
  });
});

// Spec strategy-constructor «Метка сделки выбирает строку».
describe('the mark of a trade on the chart', () => {
  const scrolled = vi.fn();

  beforeEach(() => {
    scrolled.mockClear();
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled(this.getAttribute('aria-label'));
    };
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Клик по метке выхода при открытой вкладке «Заявки»».
  it('opens «Сделки» and selects the row of the trade whose exit mark is clicked', async () => {
    await previewOrders([order(october, -0.0098), order(october + day, -0.0056)]);
    await userEvent.click(screen.getByRole('tab', { name: 'Заявки 2' }));
    expect(screen.queryByRole('table', { name: 'Сделки' })).toBeNull();

    // The trade of 08:00 ends at 12:00: its exit is on the candle that opens at 08:00.
    act(() => clickChart(october / 1000));

    expect(screen.getByRole('tab', { name: 'Сделки 2' })).toHaveAttribute('aria-selected', 'true');
    const table = screen.getByRole('table', { name: 'Сделки' });
    expect(selectedRows(table)).toEqual(['Сделка 2025-10-05 11:00']);
    await waitFor(() => expect(scrolled).toHaveBeenLastCalledWith('Сделка 2025-10-05 11:00'));
    // The marks of the chosen trade are drawn in the color of the choice, the others keep theirs.
    const colors = (drawn.markers as { time: number; color: string }[]).map((mark) => mark.color);
    expect(colors.slice(0, 2)).toEqual([chartColors.selected, chartColors.selected]);
    expect(colors.slice(2)).toEqual([chartColors.short, chartColors.exit]);
  });

  it('moves the selection to the trade of another mark, and a click on a candle without a mark keeps it', async () => {
    await previewOrders([order(october, -0.0098), order(october + day, -0.0056)]);

    act(() => clickChart((october - hour4) / 1000));
    const table = screen.getByRole('table', { name: 'Сделки' });
    expect(selectedRows(table)).toEqual(['Сделка 2025-10-05 11:00']);

    act(() => clickChart((october + day - hour4) / 1000));
    expect(selectedRows(table)).toEqual(['Сделка 2025-10-06 11:00']);

    act(() => clickChart((october + 3 * hour4) / 1000));
    expect(selectedRows(table)).toEqual(['Сделка 2025-10-06 11:00']);
  });

  it('chooses the trade of the entry when an entry and an exit of two trades are on one candle', async () => {
    // The first trade ends at 12:00, and the second is opened by the order of 12:00: both marks are on the candle of 08:00.
    await previewOrders([order(october, -0.0098), order(october + hour4, -0.0056)]);

    act(() => clickChart(october / 1000));

    expect(selectedRows(screen.getByRole('table', { name: 'Сделки' }))).toEqual([
      'Сделка 2025-10-05 15:00',
    ]);
  });

  it('still shows a trade on the chart by a click on its row, and selects it', async () => {
    await previewOrders([order(october, -0.0098), order(october + day, -0.0056)]);
    const table = screen.getByRole('table', { name: 'Сделки' });

    await userEvent.click(within(table).getByRole('button', { name: '2025-10-06 11:00' }));

    expect(selectedRows(table)).toEqual(['Сделка 2025-10-06 11:00']);
    const entry = (october + day - hour4) / 1000;
    expect(drawn.range!.from).toBeLessThan(entry);
    expect(drawn.range!.to).toBeGreaterThan(entry);
  });
});

/**
 * A ladder: a market entry of 100 USDT at 08:00, two averaging buys of 100 USDT at 10:00 and 13:00 and a sale of the
 * whole quantity at 17:00; then an entry at 17:01 still open at the end.
 */
function ladderAnswer() {
  const fill = (
    moment: number,
    action: string,
    side: string,
    price: number,
    amount: number,
    node: string,
  ) => ({
    moment,
    action,
    side,
    price,
    quantity: amount / price,
    amount,
    node,
  });
  return {
    orders: [
      {
        moment: october,
        node: 'entry',
        side: 'buy',
        outcome: 'filled',
        reason: null,
        price: 100,
        amount: 100,
        cycle: 0,
      },
      {
        moment: october + 2 * hour,
        node: 'buy',
        side: 'buy',
        outcome: 'filled',
        reason: null,
        price: 99,
        amount: 100,
        cycle: 0,
      },
      {
        moment: october + 5 * hour,
        node: 'buy',
        side: 'buy',
        outcome: 'filled',
        reason: null,
        price: 98.01,
        amount: 100,
        cycle: 0,
      },
      {
        moment: october + 9 * hour,
        node: 'sell',
        side: 'sell',
        outcome: 'filled',
        reason: null,
        price: 100.5,
        amount: 304.5,
        cycle: 0,
      },
      {
        moment: october + 9 * hour + 60_000,
        node: 'entry',
        side: 'buy',
        outcome: 'filled',
        reason: null,
        price: 100.6,
        amount: 100,
        cycle: 1,
      },
    ],
    cycles: [
      {
        side: 'long',
        fills: [
          fill(october, 'entry', 'buy', 100, 100, 'entry'),
          fill(october + 2 * hour, 'increase', 'buy', 99, 100, 'buy'),
          fill(october + 5 * hour, 'increase', 'buy', 98.01, 100, 'buy'),
          fill(october + 9 * hour, 'exit', 'sell', 100.5, 304.5, 'sell'),
        ],
        entries: 3,
        invested: 300,
        exitReason: 'limit',
        ambiguous: false,
        net: 0.015,
        netAmount: 4.5,
        open: false,
        exclusion: null,
      },
      {
        side: 'long',
        fills: [fill(october + 9 * hour + 60_000, 'entry', 'buy', 100.6, 100, 'entry')],
        entries: 1,
        invested: 100,
        exitReason: null,
        ambiguous: false,
        net: -0.002,
        netAmount: -0.2,
        open: true,
        exclusion: null,
      },
    ],
  };
}

// Change add-position-node, tasks 9.1–9.3: the cycles of a ladder in the panel.
describe('the cycles of a ladder in the preview', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Докупки на графике».
  it('marks the entry, each averaging buy and the sale of a cycle', async () => {
    await previewOrders([], ladderAnswer());

    const texts = (drawn.markers as { text: string }[]).map((mark) => mark.text);
    expect(texts).toEqual(['лонг', 'докупка', 'докупка', 'выход', 'лонг']);
  });

  // Scenario «Цикл лесенки в списке».
  it('lists a cycle with its entries, the money put in and its result in percent and USDT', async () => {
    await previewOrders([], ladderAnswer());

    const table = screen.getByRole('table', { name: 'Сделки' });
    const row = within(table).getByRole('row', { name: 'Сделка 2025-10-05 11:00' });
    expect(row).toHaveTextContent('300.00');
    expect(within(row).getAllByRole('cell')[2]).toHaveTextContent('3');
    expect(row).toHaveTextContent('лимиткой · 9h');
    expect(row).toHaveTextContent('+1.50 %');
    expect(row).toHaveTextContent('+4.50 USDT');
    expect(within(table).getByRole('row', { name: 'Сделка 2025-10-05 20:01' })).toHaveTextContent(
      'открыт',
    );
  });

  it('sums the closed cycles only, and names the node and the price of every fill', async () => {
    await previewOrders([], ladderAnswer());

    const result = within(screen.getByRole('region', { name: 'Предпросмотр' })).getByRole(
      'status',
      {
        name: 'Результат',
      },
    );
    expect(result).toHaveTextContent('Сделок 2');
    expect(result).toHaveTextContent('Итог +1.50 % · +4.50 USDT');

    await userEvent.click(screen.getByRole('tab', { name: 'Заявки 5' }));
    const list = screen.getByRole('table', { name: 'Заявки' });
    const buy = within(list).getByRole('row', { name: 'Заявка 2025-10-05 13:00' });
    expect(buy).toHaveTextContent('buy');
    expect(buy).toHaveTextContent('покупка');
    expect(buy).toHaveTextContent('99');
    expect(buy).toHaveTextContent('исполнена, цикл 1');
    expect(within(list).getByRole('row', { name: 'Заявка 2025-10-05 11:00' })).toHaveTextContent(
      'лимитка, +1.50 %',
    );
  });
});

// Change add-position-node, task 12.7: a fill in the first minute of a candle was put on the candle before it.
describe('the candle of a mark of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  /** The marks of the chart as `<day> <open time of the candle> <text>`, on candles of 4h. */
  const marks = () =>
    (drawn.markers as { time: number; text: string }[]).map(
      (mark) =>
        `${new Date(mark.time * 1000).toISOString().slice(8, 16).replace('T', ' ')} ${mark.text}`,
    );

  it('is the candle each fill of a ladder falls in', async () => {
    await previewOrders([], asWritten(ladderAnswer()));

    expect(marks()).toEqual([
      // The market order of 08:00 is filled in the first minute of the candle of 08:00.
      '05 08:00 лонг',
      '05 08:00 докупка',
      '05 12:00 докупка',
      '05 16:00 выход',
      '05 16:00 лонг',
    ]);
  });

  it('is the candle that closed for the entry of an enter and for an exit by time, and the next one for a stop', async () => {
    const stopped = order(october, -0.0098);
    await previewOrders(
      [],
      asWritten(
        withCycles([
          { ...stopped, trade: { ...stopped.trade!, exitReason: 'stop' } },
          order(october + day, -0.0056),
        ]),
      ),
    );

    expect(marks()).toEqual([
      // An enter is placed at the close of 08:00: its entry is on the candle that closed then.
      '05 04:00 шорт',
      // The stop of 12:00 is hit in the first minute of the candle of 12:00.
      '05 12:00 выход',
      '06 04:00 шорт',
      // The time of the second cycle is out at 12:00, the close of the candle of 08:00.
      '06 08:00 выход',
    ]);
  });
});

/** An answer as the API writes it: a field without a value is left out, not written as null. */
function asWritten<T>(value: T): T {
  return JSON.parse(
    JSON.stringify(value, (_key, field: unknown) => (field === null ? undefined : field)),
  ) as T;
}

// Found by the live check of change add-position-node: the API leaves a null field out of its answer.
describe('an answer of the preview without its empty fields', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('lists the trade of a plan, marks it and sums it', async () => {
    await previewOrders(
      [],
      asWritten(withCycles([order(october, -0.0021), order(october + hour, null, 'skipped')])),
    );

    const result = within(screen.getByRole('region', { name: 'Предпросмотр' })).getByRole(
      'status',
      { name: 'Результат' },
    );
    expect(result).toHaveTextContent('Сделок 1');
    expect(result).toHaveTextContent('Итог -0.21 %');
    expect(screen.getByRole('table', { name: 'Сделки' })).toHaveTextContent('по сроку · 4h');
    expect(drawn.markers).toHaveLength(2);

    const orders = await ordersTable();
    const skipped = within(orders).getByRole('row', { name: 'Заявка 2025-10-05 12:00' });
    expect(skipped).toHaveTextContent('пропущена: позиция открыта');
    expect(skipped).not.toHaveTextContent('NaN');
  });

  it('lists the cycles of a ladder with the open one', async () => {
    await previewOrders([], asWritten(ladderAnswer()));

    const table = screen.getByRole('table', { name: 'Сделки' });
    expect(within(table).getByRole('row', { name: 'Сделка 2025-10-05 11:00' })).toHaveTextContent(
      '+4.50 USDT',
    );
    const open = within(table).getByRole('row', { name: 'Сделка 2025-10-05 20:01' });
    expect(open).toHaveTextContent('открыт');
    expect(open).not.toHaveTextContent('NaN');
    expect(
      within(screen.getByRole('region', { name: 'Предпросмотр' })).getByRole('status', {
        name: 'Результат',
      }),
    ).toHaveTextContent('Итог +1.50 % · +4.50 USDT');
  });
});
