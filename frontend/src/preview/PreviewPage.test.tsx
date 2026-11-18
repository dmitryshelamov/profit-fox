import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { drawn } from '@/test/chart';

import { PreviewPage } from './PreviewPage';

const utc = (text: string) => Date.parse(`${text}Z`);

const signal = {
  formatVersion: 1,
  parameters: [{ name: 'threshold', type: 'number', value: 0.0005 }],
  outputs: [{ name: 'entry', type: 'event' }],
  nodes: [
    { id: 'p', kind: 'parameters' },
    { id: 'rate', kind: 'funding-rate' },
    { id: 'high', kind: 'compare', settings: { operation: 'ge' } },
    { id: 'first', kind: 'first-time' },
    { id: 'out', kind: 'outputs' },
  ],
  wires: [
    { from: 'rate.value', to: 'high.a' },
    { from: 'p.threshold', to: 'high.b' },
    { from: 'high.result', to: 'first.value' },
    { from: 'first.event', to: 'out.entry' },
  ],
};

const hypotheses = [
  { id: 'H0001', name: 'funding-short-4h', timeframe: '4h', signal, error: null },
  { id: 'H0002', name: 'x', timeframe: null, signal: null, error: 'signal is required' },
];

const preview = {
  instrument: 'PRLUSDT',
  timeframe: '4h',
  candles: [
    { openTime: utc('2025-11-24T00:00'), open: 1, high: 2, low: 0.5, close: 1.5, volume: 10 },
    { openTime: utc('2025-11-24T04:00'), open: 1.5, high: 2, low: 1, close: 1.75, volume: 10 },
  ],
  coverage: [{ from: utc('2023-11-18T00:00'), to: utc('2026-05-16T00:00') }],
  events: [{ moment: utc('2025-11-24T08:00') }],
  periodEnd: utc('2026-05-16T00:00'),
};

let posted: unknown[] = [];
let answer: { status: number; body: unknown } = { status: 200, body: preview };

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  posted = [];
  answer = { status: 200, body: preview };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/hypotheses') {
        return json(hypotheses);
      }

      if (path === '/api/instruments') {
        return json([{ id: 'prl', symbol: 'PRLUSDT' }]);
      }

      posted.push(await request.json());
      return json(answer.body, answer.status);
    }),
  );
});

afterEach(() => vi.unstubAllGlobals());

function renderPage() {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queries}>
      <PreviewPage />
    </QueryClientProvider>,
  );
}

async function showPair(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Пара'), 'prlusdt');
  await user.click(screen.getByRole('button', { name: 'Показать' }));
}

describe('the preview page', () => {
  it('puts the signal and the timeframe of the chosen hypothesis into the form', async () => {
    renderPage();

    const graph = screen.getByLabelText('Граф сигнала, JSON');
    await vi.waitFor(() => expect(graph).toHaveValue(JSON.stringify(signal, null, 2)));
    expect(screen.getByLabelText('Гипотеза')).toHaveValue('H0001');
    expect(screen.getByLabelText('Таймфрейм')).toHaveValue('4h');
    expect(screen.getByRole('option', { name: 'H0002 x — ошибка в файле' })).toBeDisabled();
    expect(screen.getByText('H0002: signal is required')).toBeInTheDocument();
  });

  it('sends the changed threshold and shows the events on the chart and in the list', async () => {
    const user = userEvent.setup();
    renderPage();
    const graph = screen.getByLabelText('Граф сигнала, JSON');
    await vi.waitFor(() => expect(graph).not.toHaveValue(''));

    fireEvent.change(graph, {
      target: { value: (graph as HTMLTextAreaElement).value.replace('0.0005', '0.001') },
    });
    await showPair(user);

    await screen.findByRole('table', { name: 'События' });
    expect(posted).toEqual([
      expect.objectContaining({
        instrument: 'PRLUSDT',
        timeframe: '4h',
        signal: expect.objectContaining({
          parameters: [{ name: 'threshold', type: 'number', value: 0.001 }],
        }),
      }),
    ]);
    const rows = within(screen.getByRole('table', { name: 'События' })).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('2025-11-24 08:00');
    expect(rows[1]).toHaveTextContent('1.75');
    expect(drawn.markers).toEqual([
      expect.objectContaining({ time: utc('2025-11-24T04:00') / 1000 }),
    ]);
    expect(screen.getByText(/история пары с 2025-11-24/)).toBeInTheDocument();
    expect(screen.getByText(/Разведка до 2026-05-15 включительно/)).toBeInTheDocument();
  });

  it('lists the errors of a refused graph and keeps the chart', async () => {
    const user = userEvent.setup();
    renderPage();
    await vi.waitFor(() => expect(screen.getByLabelText('Граф сигнала, JSON')).not.toHaveValue(''));
    await showPair(user);
    await screen.findByRole('table', { name: 'События' });
    const drawnBefore = drawn.setData;

    answer = {
      status: 400,
      body: {
        errors: [
          {
            code: 'UnknownNode',
            reason: 'there is no node high',
            wire: { from: 'rate.value', to: 'high.a' },
          },
          {
            code: 'UnknownNode',
            reason: 'there is no node high',
            wire: { from: 'high.result', to: 'first.value' },
          },
        ],
      },
    };
    await user.click(screen.getByRole('button', { name: 'Показать' }));

    const errors = await screen.findByRole('list', { name: 'Ошибки' });
    expect(within(errors).getByText('провод rate.value → high.a')).toBeInTheDocument();
    expect(within(errors).getByText('провод high.result → first.value')).toBeInTheDocument();
    expect(drawn.setData).toBe(drawnBefore);
    expect(screen.getByRole('table', { name: 'События' })).toBeInTheDocument();
  });

  it('refuses a text that is not JSON without asking the API', async () => {
    const user = userEvent.setup();
    renderPage();
    const graph = screen.getByLabelText('Граф сигнала, JSON');
    await vi.waitFor(() => expect(graph).not.toHaveValue(''));

    fireEvent.change(graph, { target: { value: '{ "nodes": ' } });
    await showPair(user);

    expect(await screen.findByRole('list', { name: 'Ошибки' })).toHaveTextContent('NotJson');
    expect(posted).toEqual([]);
  });
});
