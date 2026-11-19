import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { drawn } from '@/test/chart';
import { card, findNode, openFundingHigh, selectNode } from '@/test/editor';
import { bodiesOf, json } from '@/test/fakeApi';

const hour4 = 14_400_000;
const first = Date.UTC(2026, 3, 16, 0); // a candle 2026-04-16 00:00 UTC, closed 04:00

function previewOf(events: number[]) {
  return {
    instrument: 'PRLUSDT',
    timeframe: '4h',
    candles: [0, 1, 2].map((index) => ({
      openTime: first + index * hour4,
      open: 1,
      high: 2,
      low: 0.5,
      close: 1.5 + index,
      volume: 10,
    })),
    coverage: [{ from: Date.UTC(2023, 10, 18), to: Date.UTC(2026, 4, 16) }],
    events: events.map((moment) => ({ moment })),
    periodEnd: Date.UTC(2026, 4, 16),
  };
}

async function show() {
  await waitFor(() => expect(screen.getByRole('button', { name: 'Показать' })).toBeDisabled());
  await userEvent.type(screen.getByRole('combobox', { name: 'Пара' }), 'prlusdt');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Показать' })).toBeEnabled(), {
    timeout: 2000,
  });
  await userEvent.click(screen.getByRole('button', { name: 'Показать' }));
}

describe('the preview panel', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenarios «Правка порога» and «Граница разведки в панели».
  it('runs the graph of the editor and shows its events in UTC and the bounds of exploration', async () => {
    const { requests } = openFundingHigh({
      'POST /api/preview': () => json(previewOf([first + hour4])),
    });
    await findNode('Параметры', 'p');
    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    const value = card('Параметры').getByRole('textbox', { name: 'Значение параметра' });
    await userEvent.clear(value);
    await userEvent.type(value, '0.001');
    fireEvent.blur(value);

    await show();

    const panel = within(screen.getByRole('region', { name: 'Предпросмотр' }));
    expect(await panel.findByRole('button', { name: '2026-04-16 04:00' })).toBeInTheDocument();
    expect(panel.getByText(/Разведка до 2026-05-15 включительно/)).toHaveTextContent(
      'история пары с 2026-04-16',
    );
    const [body] = (await bodiesOf(requests, 'POST', '/api/preview')) as {
      instrument: string;
      timeframe: string;
      signal: { parameters: { value: number }[] };
    }[];
    expect(body!.instrument).toBe('PRLUSDT');
    expect(body!.timeframe).toBe('4h');
    expect(body!.signal.parameters[0]!.value).toBe(0.001);
    expect(drawn.markers).toHaveLength(1);
  });

  it('shows the event a click on its moment picks', async () => {
    openFundingHigh({ 'POST /api/preview': () => json(previewOf([first + hour4])) });
    await findNode('Параметры', 'p');
    await show();

    await userEvent.click(await screen.findByRole('button', { name: '2026-04-16 04:00' }));

    const mark = first / 1000;
    expect(drawn.range!.from).toBeLessThan(mark);
    expect(drawn.range!.to).toBeGreaterThan(mark);
  });

  // Scenario «Правка после предпросмотра».
  it('says the graph changed after an edit and keeps the old marks', async () => {
    openFundingHigh({ 'POST /api/preview': () => json(previewOf([first + hour4])) });
    await findNode('Параметры', 'p');
    await show();
    await screen.findByRole('button', { name: '2026-04-16 04:00' });
    expect(screen.queryByText('Граф изменён, нажмите Показать')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
    await userEvent.click(
      within(screen.getByRole('region', { name: 'Палитра' })).getByRole('button', {
        name: 'Свеча',
      }),
    );

    expect(await screen.findByText('Граф изменён, нажмите Показать')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '2026-04-16 04:00' })).toBeInTheDocument();
  });

  it('does not run a graph with errors', async () => {
    const { requests } = openFundingHigh({ 'POST /api/preview': () => json(previewOf([])) });
    const node = await selectNode('Сравнить', 'high');
    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');
    await userEvent.type(screen.getByRole('combobox', { name: 'Пара' }), 'PRLUSDT');

    await waitFor(
      () => expect(screen.getByRole('button', { name: 'Ошибки 1' })).toBeInTheDocument(),
      { timeout: 2000 },
    );
    expect(screen.getByRole('button', { name: 'Показать' })).toBeDisabled();
    fireEvent.keyDown(window, { key: 'Enter', ctrlKey: true });
    expect(requests.some((request) => new URL(request.url).pathname === '/api/preview')).toBe(
      false,
    );
  });

  it('runs on Ctrl+Enter', async () => {
    const { requests } = openFundingHigh({ 'POST /api/preview': () => json(previewOf([])) });
    await findNode('Параметры', 'p');
    await userEvent.type(screen.getByRole('combobox', { name: 'Пара' }), 'PRLUSDT');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Показать' })).toBeEnabled(), {
      timeout: 2000,
    });

    fireEvent.keyDown(window, { key: 'Enter', ctrlKey: true });

    await waitFor(() =>
      expect(requests.some((request) => new URL(request.url).pathname === '/api/preview')).toBe(
        true,
      ),
    );
    expect(await screen.findByText('Событий нет.')).toBeInTheDocument();
  });
});
