import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { drawn } from '@/test/chart';
import { findNode, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { savedFundingShort, strategyRoutes } from '@/test/strategies';

const hour = 3_600_000;
const hour4 = 4 * hour;
const first = Date.UTC(2026, 3, 16, 0); // a candle 2026-04-16 00:00 UTC, closed 04:00

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
    orders: [
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
    ],
    periodEnd: Date.UTC(2026, 4, 16),
    computedFrom: Date.UTC(2026, 3, 13),
  };
}

function openFundingShort() {
  const requests = fakeApi({
    ...baseRoutes(),
    ...strategyRoutes(),
    'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
    'POST /api/preview/strategy': () => json(strategyPreview()),
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

    const orders = await screen.findByRole('table', { name: 'Заявки' });
    const opened = within(orders).getByRole('row', { name: 'Заявка 2026-04-16 04:00' });
    expect(within(opened).getByText('шорт')).toBeInTheDocument();
    expect(within(opened).getByText('срок, -0.21 %')).toBeInTheDocument();
    expect(drawn.markers).toEqual([
      expect.objectContaining({ time: first / 1000, shape: 'arrowDown', text: 'шорт' }),
      expect.objectContaining({ time: (first + hour4) / 1000, shape: 'circle', text: 'выход' }),
    ]);
    const panel = within(screen.getByRole('region', { name: 'Предпросмотр' }));
    expect(panel.getByText(/заявок 3 · сделок 1/)).toBeInTheDocument();
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

    await userEvent.click(within(opened).getByRole('button', { name: '2026-04-16 04:00' }));
    expect(drawn.range!.from).toBeLessThan(first / 1000);
    expect(drawn.range!.to).toBeGreaterThan(first / 1000);
  });

  // Scenario «Пропущенная заявка в списке».
  it('marks a skipped order as skipped, without marks on the chart', async () => {
    openFundingShort();
    await findNode('funding-high', 's');

    await show();

    const orders = await screen.findByRole('table', { name: 'Заявки' });
    expect(within(orders).getByRole('row', { name: 'Заявка 2026-04-16 06:00' })).toHaveTextContent(
      'пропущена: позиция открыта',
    );
    expect(within(orders).getByRole('row', { name: 'Заявка 2026-04-16 12:00' })).toHaveTextContent(
      'без сделки: срок за концом разведки',
    );
    expect(drawn.markers).toHaveLength(2);
  });

  // Scenario «Устаревший предпросмотр».
  it('says the graph changed after the time of close-after changed and keeps the old marks', async () => {
    openFundingShort();
    await findNode('funding-high', 's');
    await show();
    await screen.findByRole('table', { name: 'Заявки' });
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
