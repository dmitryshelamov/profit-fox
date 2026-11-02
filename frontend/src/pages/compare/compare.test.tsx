import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createChart } from 'lightweight-charts';
import type { Mock } from 'vitest';

import { clearComparison, formatItems, toggleComparison } from '@/modules/comparison';
import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  backtestFull,
  backtestId,
  botDetails,
  botId,
  emaSummary,
  metricsOf,
  otherBacktestId,
  resultOf,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const ladderBotId = '55555555-5555-4555-8555-555555555555';
const ladderBot = { ...botDetails, id: ladderBotId, name: 'ETH Ladder', symbol: 'ETHUSDT' };

const curve = (count = 3) =>
  Array.from({ length: count }, (_, index) => ({
    at: new Date(Date.UTC(2024, 0, 1, index + 1)).toISOString(),
    equity: String(1000 + index),
    minEquity: '990',
    openTime: new Date(Date.UTC(2024, 0, 1, index)).toISOString(),
    open: '100',
    high: '110',
    low: '95',
    close: '105',
  }));

const detailsOf = () => json({ metrics: metricsOf(), positions: [], equity: curve() });

const engineConflict = () =>
  problem(
    409,
    'The backtest was computed at engine version 2, and the details would be computed at engine version 3.',
  );

interface Overrides {
  first?: Record<string, unknown>;
  second?: Record<string, unknown>;
  firstDetails?: () => Response | Promise<Response>;
  secondDetails?: () => Response | Promise<Response>;
  secondBacktest?: () => Response;
}

/** Two backtests of two bots: the result #4 of the EMA bot and the result #212 of the ETH Ladder bot. */
function routes(overrides: Overrides = {}) {
  return {
    [`GET /api/backtests/${backtestId}`]: () =>
      json(backtestFull({ status: 'Succeeded', ...overrides.first })),
    [`GET /api/backtests/${backtestId}/results/4`]: () => json(resultOf(4, { return: '18.42' })),
    [`GET /api/backtests/${backtestId}/results/4/details`]: overrides.firstDetails ?? detailsOf,
    [`GET /api/backtests/${otherBacktestId}`]:
      overrides.secondBacktest ??
      (() =>
        json(
          backtestFull({
            id: otherBacktestId,
            botId: ladderBotId,
            status: 'Succeeded',
            createdAt: '2026-10-30T18:30:00Z',
            ...overrides.second,
          }),
        )),
    [`GET /api/backtests/${otherBacktestId}/results/212`]: () =>
      json({
        ...resultOf(212, { return: '11.20', maxDrawdown: '9.95' }),
        parameterValues: { step: 50, levels: 5 },
      }),
    [`GET /api/backtests/${otherBacktestId}/results/212/details`]:
      overrides.secondDetails ?? detailsOf,
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    [`GET /api/bots/${ladderBotId}`]: () => json(ladderBot),
    'GET /api/strategies': () => json([emaSummary]),
  };
}

const both = [
  { backtestId, combination: 4 },
  { backtestId: otherBacktestId, combination: 212 },
];

const address = (items = both) => `/compare?items=${formatItems(items)}`;

describe('compare page', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearComparison();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('gives each result its own color on the chart and the same swatch in its column', async () => {
    const colors = ['#111111', '#222222'];
    colors.forEach((color, index) =>
      document.documentElement.style.setProperty(`--color-series-${index + 1}`, color),
    );
    fakeApi(routes());
    renderApp(address());

    const headers = await screen.findAllByRole('columnheader', { name: /BTC EMA|ETH Ladder/ });
    await waitFor(() =>
      expect(screen.getByRole('figure', { name: 'Капитал' })).toHaveAttribute('data-lines', '2'),
    );

    const swatches = headers.map((header) => header.querySelector('[data-series]')!);
    expect(swatches.map((swatch) => swatch.getAttribute('data-series'))).toEqual([
      '--color-series-1',
      '--color-series-2',
    ]);
    const chart = vi.mocked(createChart).mock.results.at(-1)!.value as { addSeries: Mock };
    const drawn = chart.addSeries.mock.calls
      .slice(-colors.length)
      .map(([, options]) => (options as { color: string }).color);
    expect(drawn).toEqual(colors);
    colors.forEach((_, index) =>
      document.documentElement.style.removeProperty(`--color-series-${index + 1}`),
    );
  });

  // Scenario "Ссылка на сравнение".
  it('shows the results of the address and makes them the set of the tab', async () => {
    toggleComparison({ backtestId, combination: 0 });
    fakeApi(routes());
    renderApp(address());

    expect(await screen.findByRole('columnheader', { name: /BTC EMA час/ })).toBeInTheDocument();
    expect(await screen.findByRole('columnheader', { name: /ETH Ladder/ })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Сравнение' })).toHaveTextContent('2 из 6');
  });

  it('shows the number and the date of the backtest of each result and its metrics', async () => {
    fakeApi(routes());
    renderApp(address());

    const first = await screen.findByRole('columnheader', { name: /BTC EMA час/ });
    expect(first).toHaveTextContent('#4 · 02.11 14:05');
    const second = await screen.findByRole('columnheader', { name: /ETH Ladder/ });
    expect(second).toHaveTextContent('#212 · 30.10 18:30');
    const row = (await screen.findByText('Доходность')).closest('tr')!;
    expect(
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['+18.42 %', '+11.20 %']);
    const drawdown = screen.getByText('Макс. просадка').closest('tr')!;
    expect(within(drawdown).getAllByRole('cell')[1]).toHaveTextContent('9.95 %');
  });

  // Scenario "Разные стратегии".
  it('joins the parameters of all results and puts a dash where a strategy has none', async () => {
    fakeApi(routes());
    renderApp(address());

    const cells = async (name: string) =>
      within((await screen.findByRole('rowheader', { name })).closest('tr')!)
        .getAllByRole('cell')
        .map((cell) => cell.textContent);
    expect(await cells('fast')).toEqual(['10', '—']);
    expect(await cells('step')).toEqual(['—', '50']);
    expect(await cells('levels')).toEqual(['—', '5']);
  });

  it('marks a result of a bot that changed', async () => {
    fakeApi(routes({ second: { botChanged: true } }));
    renderApp(address());

    const second = await screen.findByRole('columnheader', { name: /ETH Ladder/ });
    await waitFor(() => expect(second).toHaveTextContent('бот изменился'));
    expect(screen.getByRole('columnheader', { name: /BTC EMA час/ })).not.toHaveTextContent(
      'бот изменился',
    );
  });

  it('shows the table at once and draws the curves as they arrive', async () => {
    let release: (response: Response) => void = () => undefined;
    fakeApi(
      routes({ secondDetails: () => new Promise<Response>((resolve) => (release = resolve)) }),
    );
    renderApp(address());

    await screen.findByRole('columnheader', { name: /ETH Ladder/ });
    const chart = await screen.findByRole('figure', { name: 'Капитал' });
    await waitFor(() => expect(chart).toHaveAttribute('data-lines', '1'));
    expect(screen.getByText('Кривые загружаются…')).toBeInTheDocument();

    await act(async () => release(detailsOf()));

    await waitFor(() => expect(chart).toHaveAttribute('data-lines', '2'));
    expect(screen.queryByText('Кривые загружаются…')).not.toBeInTheDocument();
  });

  // Scenario "Результат другой версии движка".
  it('keeps a result of another engine in the table without a curve and names the versions', async () => {
    fakeApi(routes({ secondDetails: engineConflict }));
    renderApp(address());

    expect(await screen.findByRole('columnheader', { name: /ETH Ladder/ })).toHaveTextContent(
      'движок v2',
    );
    const note = await screen.findByText(/ETH Ladder #212: кривой нет/);
    expect(note).toHaveTextContent('движком v2');
    expect(note).toHaveTextContent('движком v3');
    const chart = screen.getByRole('figure', { name: 'Капитал' });
    await waitFor(() => expect(chart).toHaveAttribute('data-lines', '1'));
    expect(screen.getByText('+11.20 %')).toBeInTheDocument();
  });

  it('says why a curve is missing when the refusal is of another kind', async () => {
    fakeApi(routes({ secondDetails: () => problem(500, 'Сбой расчёта.') }));
    renderApp(address());

    expect(
      await screen.findByText(/ETH Ladder #212: кривой нет — Сбой расчёта\./),
    ).toBeInTheDocument();
  });

  // Scenario "Бот удалён".
  it('says a result is not found and takes it out of the set', async () => {
    fakeApi(routes({ secondBacktest: () => problem(404, 'нет такого') }));
    const { history } = renderApp(address());

    expect(await screen.findByRole('status')).toHaveTextContent(
      `Результат #212 бэктеста ${otherBacktestId.slice(0, 8)} не найден — убран из набора.`,
    );
    await waitFor(() =>
      expect(history.location.search).toBe(`?items=${encodeURIComponent(`${backtestId}:4`)}`),
    );
    expect(screen.queryByRole('columnheader', { name: /ETH Ladder/ })).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Сравнение' })).toHaveTextContent('1 из 6');
  });

  // Scenario "Комбинация без результата": the result is not saved, though the backtest exists.
  it('takes out a result the backtest has no saved result for', async () => {
    fakeApi({
      ...routes(),
      [`GET /api/backtests/${otherBacktestId}/results/212`]: () => problem(404, 'нет'),
    });
    renderApp(address());

    expect(await screen.findByRole('status')).toHaveTextContent('Результат #212');
  });

  // Scenario "Результат после пропуска": the number is asked for as it is, not as a position among the saved results.
  it('reads each result by its own number and never by a position in the list', async () => {
    const requests = fakeApi(routes());
    renderApp(address());

    expect(await screen.findByRole('columnheader', { name: /ETH Ladder/ })).toBeInTheDocument();
    const paths = requests.map((request) => new URL(request.url).pathname);
    expect(paths).toContain(`/api/backtests/${otherBacktestId}/results/212`);
    expect(paths).toContain(`/api/backtests/${backtestId}/results/4`);
    expect(paths.filter((path) => path.endsWith('/results'))).toEqual([]);
  });

  it('colors the return and the pnl of each result by their sign', async () => {
    fakeApi({
      ...routes(),
      [`GET /api/backtests/${otherBacktestId}/results/212`]: () =>
        json(resultOf(212, { return: '-3.10', pnl: '0.00' })),
    });
    renderApp(address());

    const row = (title: string) =>
      within(screen.getByRole('rowheader', { name: title }).closest('tr')!);
    await waitFor(() => expect(row('Доходность').getByText('+18.42 %')).toBeInTheDocument());
    expect(row('Доходность').getByText('+18.42 %')).toHaveClass('text-ok');
    expect(row('Доходность').getByText('-3.10 %')).toHaveClass('text-err');
    expect(row('PnL').getByText('+184.20')).toHaveClass('text-ok');
    expect(row('PnL').getByText('0.00')).not.toHaveClass('text-ok', 'text-err');
  });

  it('removes a result from the set on request', async () => {
    fakeApi(routes());
    const { history } = renderApp(address());

    await userEvent.click(
      await screen.findByRole('button', { name: 'Убрать результат #212 из сравнения' }),
    );

    expect(screen.queryByRole('columnheader', { name: /ETH Ladder/ })).not.toBeInTheDocument();
    expect(history.location.search).toBe(`?items=${encodeURIComponent(`${backtestId}:4`)}`);
    expect(screen.getByRole('region', { name: 'Сравнение' })).toHaveTextContent('1 из 6');
  });

  it('clears the set', async () => {
    fakeApi(routes());
    renderApp(address());

    await userEvent.click(await screen.findByRole('button', { name: 'Очистить набор' }));

    expect(
      screen.getByText('Набор пуст. Отметьте результаты в таблице бэктеста.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Сравнение' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('skips entries of the address that are not results', async () => {
    fakeApi(routes());
    renderApp(`/compare?items=nonsense,${backtestId}:4`);

    expect(await screen.findByRole('columnheader', { name: /BTC EMA час/ })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Сравнение' })).toHaveTextContent('1 из 6');
  });
});
