import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { clickChart, drawn } from '@/test/chart';
import { card, findNode, openFundingHigh, selectNode } from '@/test/editor';
import { bodiesOf, json } from '@/test/fakeApi';

import { chartColors } from './CandleChart';

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
    periodEnd: Date.UTC(2026, 10, 22),
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
    expect(panel.getByText(/История до 2026-11-21 включительно/)).toHaveTextContent(
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

// Spec signal-constructor «Число событий в шапке панели предпросмотра».
describe('the number of events in the header of the preview panel', () => {
  afterEach(() => vi.unstubAllGlobals());

  const panel = () => within(screen.getByRole('region', { name: 'Предпросмотр' }));

  // Scenarios «События на паре» and «До предпросмотра».
  it('is not there before the first preview and counts the events after it', async () => {
    openFundingHigh({
      'POST /api/preview': () =>
        json(previewOf(Array.from({ length: 7 }, (_, index) => first + (index + 1) * hour4))),
    });
    await findNode('Параметры', 'p');
    expect(panel().queryByRole('status', { name: 'Результат' })).toBeNull();
    expect(panel().queryByText(/Событий/)).toBeNull();

    await show();

    expect(await panel().findByRole('status', { name: 'Результат' })).toHaveTextContent(
      'Событий 7',
    );
  });

  it('stays after an edit of the graph, next to the note that the graph changed', async () => {
    openFundingHigh({ 'POST /api/preview': () => json(previewOf([first + hour4])) });
    await findNode('Параметры', 'p');
    await show();
    await panel().findByRole('status', { name: 'Результат' });

    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    const value = card('Параметры').getByRole('textbox', { name: 'Значение параметра' });
    await userEvent.clear(value);
    await userEvent.type(value, '0.002');
    fireEvent.blur(value);

    expect(await panel().findByText('Граф изменён, нажмите Показать')).toBeInTheDocument();
    expect(panel().getByRole('status', { name: 'Результат' })).toHaveTextContent('Событий 1');
  });
});

// Change redesign-bench-ui, task 6.1: the header of the panel in the shared parts.
describe('the header of the preview panel', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('chooses the timeframe by a segment of the group «Таймфрейм» and remembers it', async () => {
    const { requests } = openFundingHigh({ 'POST /api/preview': () => json(previewOf([])) });
    await findNode('Параметры', 'p');
    const timeframe = within(screen.getByRole('radiogroup', { name: 'Таймфрейм' }));
    expect(timeframe.getAllByRole('radio').map((radio) => radio.textContent)).toEqual([
      '5m',
      '15m',
      '1h',
      '4h',
      '1d',
    ]);
    expect(timeframe.getByRole('radio', { name: '4h' })).toHaveAttribute('aria-checked', 'true');

    await userEvent.click(timeframe.getByRole('radio', { name: '1h' }));
    await show();

    await waitFor(async () =>
      expect(
        ((await bodiesOf(requests, 'POST', '/api/preview')) as { timeframe: string }[]).map(
          (body) => body.timeframe,
        ),
      ).toEqual(['1h']),
    );
    expect(timeframe.getByRole('radio', { name: '1h' })).toHaveAttribute('aria-checked', 'true');
    expect(localStorage.getItem('profit-fox:preview:funding-high')).toContain('"timeframe":"1h"');
  });

  it('has «Показать» of the main look, and says why it is off while the graph has an error', async () => {
    openFundingHigh();
    const node = await selectNode('Сравнить', 'high');
    expect(screen.getByRole('button', { name: 'Показать' })).toHaveAttribute(
      'data-variant',
      'primary',
    );
    await userEvent.type(screen.getByRole('combobox', { name: 'Пара' }), 'PRLUSDT');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Показать' })).toBeEnabled(), {
      timeout: 2000,
    });
    expect(screen.queryByText('Сначала исправьте ошибку графа')).toBeNull();

    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');

    expect(
      await screen.findByText('Сначала исправьте ошибку графа', {}, { timeout: 2000 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Показать' })).toBeDisabled();
  });
});

// Spec signal-constructor «Метка события выбирает строку».
describe('the mark of an event on the chart', () => {
  const scrolled = vi.fn();
  const october = Date.UTC(2025, 9, 5, 8); // an event at 2025-10-05 08:00 UTC

  beforeEach(() => {
    scrolled.mockClear();
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled(this.getAttribute('aria-label'));
    };
  });

  afterEach(() => vi.unstubAllGlobals());

  /** A preview of 40 events four hours apart; the event of 2025-10-05 08:00 is the 31st. */
  async function fortyEvents() {
    const start = october - 30 * hour4;
    openFundingHigh({
      'POST /api/preview': () =>
        json({
          ...previewOf([]),
          candles: Array.from({ length: 44 }, (_, index) => ({
            openTime: start - hour4 + index * hour4,
            open: 1,
            high: 2,
            low: 0.5,
            close: 1.5,
            volume: 10,
          })),
          events: Array.from({ length: 40 }, (_, index) => ({ moment: start + index * hour4 })),
        }),
    });
    await findNode('Параметры', 'p');
    await show();
    return screen.findByRole('table', { name: 'События' });
  }

  const selected = (table: HTMLElement) =>
    within(table)
      .getAllByRole('row')
      .filter((row) => row.getAttribute('aria-selected') === 'true')
      .map((row) => row.getAttribute('aria-label'));

  // Scenario «Клик по метке».
  it('selects the row of the event and brings it into view', async () => {
    const table = await fortyEvents();
    expect(within(table).getAllByRole('row', { name: /^Событие / })).toHaveLength(40);
    expect(selected(table)).toEqual([]);

    // The event of 08:00 closed the candle that opens at 04:00.
    act(() => clickChart((october - hour4) / 1000));

    expect(selected(table)).toEqual(['Событие 2025-10-05 08:00']);
    await waitFor(() => expect(scrolled).toHaveBeenLastCalledWith('Событие 2025-10-05 08:00'));
    const colors = (drawn.markers as { color: string }[]).map((mark) => mark.color);
    expect(colors.filter((color) => color === chartColors.selected)).toHaveLength(1);
    expect(colors[30]).toBe(chartColors.selected);
    expect(colors[0]).toBe(chartColors.event);
  });

  it('moves the selection by a click on another mark or on a row, and keeps it on a candle without a mark', async () => {
    const table = await fortyEvents();
    act(() => clickChart((october - hour4) / 1000));

    act(() => clickChart(october / 1000));
    expect(selected(table)).toEqual(['Событие 2025-10-05 12:00']);

    await userEvent.click(within(table).getByRole('button', { name: '2025-10-05 08:00' }));
    expect(selected(table)).toEqual(['Событие 2025-10-05 08:00']);
    expect(drawn.range!.from).toBeLessThan((october - hour4) / 1000);
    expect(drawn.range!.to).toBeGreaterThan((october - hour4) / 1000);

    // A candle a year earlier has no event.
    act(() => clickChart((october - 365 * 24 * 3_600_000) / 1000));
    expect(selected(table)).toEqual(['Событие 2025-10-05 08:00']);
  });

  it('brings the row into view again by a second click on the same mark', async () => {
    await fortyEvents();
    act(() => clickChart((october - hour4) / 1000));
    await waitFor(() => expect(scrolled).toHaveBeenCalledTimes(1));

    // The list was scrolled away from the selected row; the mark is clicked once more.
    act(() => clickChart((october - hour4) / 1000));

    await waitFor(() => expect(scrolled).toHaveBeenCalledTimes(2));
    expect(scrolled).toHaveBeenLastCalledWith('Событие 2025-10-05 08:00');
  });
});
