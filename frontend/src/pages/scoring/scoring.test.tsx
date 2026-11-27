import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { ScoringLabel } from '@/api/scoring';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

function label(overrides: Partial<ScoringLabel> & { text: string }): ScoringLabel {
  return {
    enabled: true,
    timeframe: '1h',
    signal: 'some-signal',
    longWeight: 0,
    shortWeight: 0,
    builtIn: false,
    ...overrides,
  };
}

const burst = label({
  text: '⚡ всплеск',
  timeframe: null,
  signal: null,
  builtIn: true,
});

const labels = [
  label({ text: '↓ BTC 4h', timeframe: '4h', signal: 'btc-down-4h' }),
  label({ text: 'оборот ↑ 1h', signal: 'turnover-high-1h', longWeight: 1 }),
  label({ text: 'фандинг', signal: 'funding-cold', enabled: false }),
  burst,
];

function scores(rows: readonly (readonly [number, number])[]) {
  return json({
    rows: rows.map(([score, ideas]) => ({ score, ideas, wins: 0.5, mean: 0.002, sum: 1.5 })),
    filterGone: false,
  });
}

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/scoring/labels': () => json(labels),
    'GET /api/ideas/scores': () =>
      scores([
        [2, 40],
        [0, 12],
      ]),
    ...extra,
  });
}

const queriesOf = (requests: readonly Request[], path: string) =>
  requests
    .map((request) => new URL(request.url))
    .filter((url) => url.pathname === path)
    .map((url) => url.searchParams);

// Change add-idea-score, tasks 3.3 and 3.4: spec idea-score «Раздел «Скоринг»» and «Итоги по баллу на странице «Скоринг»».
describe('the section «Скоринг»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Таблица меток».
  it('has a row for each label, the one that is off marked, and «⚡ всплеск» last', async () => {
    routes();
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Веса меток' }));
    const rows = table.getAllByRole('row').slice(1);
    expect(rows.map((row) => row.getAttribute('aria-label'))).toEqual([
      '↓ BTC 4h',
      'оборот ↑ 1h',
      'фандинг',
      '⚡ всплеск',
    ]);
    expect(within(rows[0]!).getByText('4h')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('btc-down-4h')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('выключена')).toBeInTheDocument();
    expect(within(rows[3]!).getByText('встроенная метка')).toBeInTheDocument();
    expect(table.getByRole('combobox', { name: 'Вес лонга оборот ↑ 1h' })).toHaveValue('1');
    expect(
      within(table.getByRole('combobox', { name: 'Вес шорта ↓ BTC 4h' }))
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['-3', '-2', '-1', '0', '+1', '+2', '+3']);
  });

  // Scenario «Правка веса».
  it('saves a weight changed in its row and reads the outcomes by score again', async () => {
    const requests = routes({
      'PUT /api/scoring/weights': () =>
        json(label({ text: '↓ BTC 4h', timeframe: '4h', signal: 'btc-down-4h', longWeight: -2 })),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Итоги по баллу' });
    const before = queriesOf(requests, '/api/ideas/scores').length;

    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Вес лонга ↓ BTC 4h' }),
      '-2',
    );

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([
        { text: '↓ BTC 4h', longWeight: -2, shortWeight: 0 },
      ]),
    );
    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas/scores').length).toBeGreaterThan(before),
    );
    expect(screen.getByRole('combobox', { name: 'Вес лонга ↓ BTC 4h' })).toHaveValue('-2');
    expect(screen.getByRole('combobox', { name: 'Вес лонга ↓ BTC 4h' }).parentElement).toHaveClass(
      '[&>select]:text-err',
    );
  });

  // Scenario «Отказ сервера».
  it('keeps the weight before and tells the refusal in the row', async () => {
    routes({
      'PUT /api/scoring/weights': () =>
        json(
          {
            errors: [
              {
                code: 'InvalidWeight',
                reason: 'вес шорта — целое от -3 до +3',
                field: 'shortWeight',
              },
            ],
          },
          422,
        ),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Вес шорта фандинг' }), '2');

    const row = screen.getByRole('row', { name: 'фандинг' });
    expect(await within(row).findByRole('alert')).toHaveTextContent(
      /Вес не сохранён.*Стоит прежний вес/,
    );
    expect(within(row).getByRole('combobox', { name: 'Вес шорта фандинг' })).toHaveValue('0');
  });

  it('keeps the weight before when the server does not answer', async () => {
    routes({ 'PUT /api/scoring/weights': () => json({ title: 'down' }, 503) });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Вес лонга ⚡ всплеск' }),
      '3',
    );

    const row = screen.getByRole('row', { name: '⚡ всплеск' });
    expect(await within(row).findByRole('alert')).toHaveTextContent(
      'Вес не сохранён: сервер не ответил. Стоит прежний вес.',
    );
    expect(within(row).getByRole('combobox', { name: 'Вес лонга ⚡ всплеск' })).toHaveValue('0');
  });

  // Scenario «Меток нет».
  it('has only «⚡ всплеск» without labels and leads to «Сигналы»', async () => {
    routes({ 'GET /api/scoring/labels': () => json([burst]) });
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Веса меток' }));
    expect(
      table
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.getAttribute('aria-label')),
    ).toEqual(['⚡ всплеск']);
    expect(screen.getByText(/метки включают в списке сигналов/)).toBeInTheDocument();
    const weights = within(screen.getByRole('region', { name: 'Веса меток' }));
    expect(weights.getByRole('link', { name: 'Сигналы' })).toHaveAttribute('href', '/signals');
  });
});

describe('the outcomes by score on «Скоринг»', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows each score with its ideas, the share in the plus, the mean and the sum, over 30 days at first', async () => {
    const requests = routes();
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Итоги по баллу' }));
    expect(
      within(table.getByRole('row', { name: 'Балл +2' }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['+2', '40', '50 %', '+0.20 %', '+1.50']);
    expect(queriesOf(requests, '/api/ideas/scores').at(-1)!.get('period')).toBe('30');
    expect(screen.getByText('52 идей')).toBeInTheDocument();
  });

  // Scenario «Малая выборка».
  it('marks a score with fewer than 30 ideas as a small sample', async () => {
    routes();
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Итоги по баллу' }));
    expect(
      within(table.getByRole('row', { name: 'Балл 0' })).getByText('мало идей < 30'),
    ).toBeInTheDocument();
    expect(within(table.getByRole('row', { name: 'Балл +2' })).queryByText(/мало идей/)).toBeNull();
  });

  // Scenario «Выбор периода».
  it('asks the outcomes of 7 days with the side and the timeframe chosen', async () => {
    const requests = routes();
    renderApp('/scoring');
    const section = within(await screen.findByRole('region', { name: 'Итоги по баллу' }));

    await userEvent.selectOptions(section.getByRole('combobox', { name: 'Период' }), '7');
    await userEvent.selectOptions(section.getByRole('combobox', { name: 'Сторона' }), 'short');
    await userEvent.selectOptions(section.getByRole('combobox', { name: 'Таймфрейм' }), '4h');

    await waitFor(() => {
      const query = queriesOf(requests, '/api/ideas/scores').at(-1)!;
      expect([query.get('period'), query.get('side'), query.get('timeframe')]).toEqual([
        '7',
        'short',
        '4h',
      ]);
    });
  });

  // Scenario «Закрытых идей нет».
  it('says there are no closed ideas over the period', async () => {
    routes({ 'GET /api/ideas/scores': () => scores([]) });
    renderApp('/scoring');

    expect(await screen.findByText('Закрытых идей за период нет.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Итоги по баллу' })).toBeNull();
  });
});
