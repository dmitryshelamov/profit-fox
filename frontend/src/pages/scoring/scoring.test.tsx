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

// The outcomes by score in groups of 0.5 (change decimal-label-weights, design.md, decision 4): each by its lower bound.
function scores(rows: readonly (readonly [number, number])[]) {
  return json({
    rows: rows.map(([from, ideas]) => ({
      from,
      to: from + 0.5,
      ideas,
      wins: 0.5,
      mean: 0.002,
      sum: 1.5,
    })),
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
        [0.5, 12],
      ]),
    ...extra,
  });
}

// Types into the field of a weight in place of its number.
async function type(name: string, text: string) {
  const field = screen.getByRole('textbox', { name });
  await userEvent.clear(field);
  await userEvent.type(field, text);
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
    expect(table.getByRole('textbox', { name: 'Вес лонга оборот ↑ 1h' })).toHaveValue('1');
    // Change decimal-label-weights, task 4.3 (design.md, decision 5): a weight is typed as a decimal number; the range and
    // the hundredths are the server's to check (scenario «Вес дальше сотых»).
    expect(table.getByRole('textbox', { name: 'Вес шорта ↓ BTC 4h' })).toHaveAttribute(
      'inputmode',
      'decimal',
    );
  });

  // Scenario «Правка веса» (change decimal-label-weights, task 4.3): a number typed in the row is saved by Enter.
  it('saves a weight typed in its row on Enter and reads the outcomes by score again', async () => {
    const requests = routes({
      'PUT /api/scoring/weights': () =>
        json(label({ text: '↓ BTC 4h', timeframe: '4h', signal: 'btc-down-4h', longWeight: -0.5 })),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Итоги по баллу' });
    const before = queriesOf(requests, '/api/ideas/scores').length;

    const field = screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' });
    await userEvent.clear(field);
    await userEvent.type(field, '-0.5{Enter}');

    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' }).parentElement).toHaveClass(
        '[&>input]:text-err',
      ),
    );
    expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([
      { text: '↓ BTC 4h', longWeight: -0.5, shortWeight: 0 },
    ]);
    await waitFor(() =>
      expect(queriesOf(requests, '/api/ideas/scores').length).toBeGreaterThan(before),
    );
    expect(screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' })).toHaveValue('-0.5');
    await userEvent.tab();
    expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toHaveLength(1);
  });

  it('saves a weight typed in its row when the field is left', async () => {
    const requests = routes({
      'PUT /api/scoring/weights': () =>
        json(
          label({
            text: 'оборот ↑ 1h',
            signal: 'turnover-high-1h',
            longWeight: 1,
            shortWeight: 0.25,
          }),
        ),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    const field = screen.getByRole('textbox', { name: 'Вес шорта оборот ↑ 1h' });
    await userEvent.clear(field);
    await userEvent.type(field, '0.25');
    await userEvent.tab();

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([
        { text: 'оборот ↑ 1h', longWeight: 1, shortWeight: 0.25 },
      ]),
    );
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Вес шорта оборот ↑ 1h' })).toHaveValue('0.25'),
    );
  });

  // A Russian keyboard types a decimal comma.
  it('reads a weight typed with a comma', async () => {
    const requests = routes({
      'PUT /api/scoring/weights': () =>
        json(label({ text: '↓ BTC 4h', timeframe: '4h', signal: 'btc-down-4h', longWeight: 0.25 })),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    const field = screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' });
    await userEvent.clear(field);
    await userEvent.type(field, '0,25{Enter}');

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([
        { text: '↓ BTC 4h', longWeight: 0.25, shortWeight: 0 },
      ]),
    );
  });

  // Review of change decimal-label-weights: the short typed while the long is being saved keeps the long typed.
  it('sends the long typed before with the short typed while the long is being saved', async () => {
    let answer: (() => void) | undefined;
    const requests = routes({
      'PUT /api/scoring/weights': async (request) => {
        const body = (await request.json()) as { longWeight: number; shortWeight: number };
        if (answer === undefined) {
          await new Promise<void>((resolve) => {
            answer = resolve;
          });
        }
        return json(
          label({
            text: '↓ BTC 4h',
            timeframe: '4h',
            signal: 'btc-down-4h',
            longWeight: body.longWeight,
            shortWeight: body.shortWeight,
          }),
        );
      },
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    const long = screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' });
    await userEvent.clear(long);
    await userEvent.type(long, '1.5{Enter}');
    await waitFor(() => expect(answer).toBeDefined());
    const short = screen.getByRole('textbox', { name: 'Вес шорта ↓ BTC 4h' });
    await userEvent.clear(short);
    await userEvent.type(short, '2{Enter}');
    answer!();

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([
        { text: '↓ BTC 4h', longWeight: 1.5, shortWeight: 0 },
        { text: '↓ BTC 4h', longWeight: 1.5, shortWeight: 2 },
      ]),
    );
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Вес лонга ↓ BTC 4h' })).toHaveValue('1.5'),
    );
  });

  // Scenario «Уход из поля без правки».
  it('sends nothing when the field is left without a change', async () => {
    const requests = routes();
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    await userEvent.click(screen.getByRole('textbox', { name: 'Вес лонга оборот ↑ 1h' }));
    await userEvent.tab();
    const field = screen.getByRole('textbox', { name: 'Вес шорта фандинг' });
    await userEvent.clear(field);
    await userEvent.type(field, '0.00{Enter}');
    await userEvent.clear(field);
    await userEvent.tab();

    expect(await bodiesOf(requests, 'PUT', '/api/scoring/weights')).toEqual([]);
    expect(screen.getByRole('textbox', { name: 'Вес шорта фандинг' })).toHaveValue('0');
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
                reason: 'вес от −100 до +100, не больше двух знаков после точки',
                field: 'shortWeight',
              },
            ],
          },
          422,
        ),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    await type('Вес шорта фандинг', '0.125{Enter}');

    const row = screen.getByRole('row', { name: 'фандинг' });
    expect(await within(row).findByRole('alert')).toHaveTextContent(
      'Вес не сохранён: вес от −100 до +100, не больше двух знаков после точки. Стоит прежний вес.',
    );
    expect(within(row).getByRole('textbox', { name: 'Вес шорта фандинг' })).toHaveValue('0');
  });

  it('keeps the weight before when the server does not answer', async () => {
    routes({ 'PUT /api/scoring/weights': () => json({ title: 'down' }, 503) });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });

    await type('Вес лонга ⚡ всплеск', '3{Enter}');

    const row = screen.getByRole('row', { name: '⚡ всплеск' });
    expect(await within(row).findByRole('alert')).toHaveTextContent(
      'Вес не сохранён: сервер не ответил. Стоит прежний вес.',
    );
    expect(within(row).getByRole('textbox', { name: 'Вес лонга ⚡ всплеск' })).toHaveValue('0');
  });

  // Task 8.7 (review, P3): the label is gone — renamed or its signal deleted — while the page was open.
  it('tells in the row that the label is gone and reads the labels again on a 404', async () => {
    const requests = routes({
      'PUT /api/scoring/weights': () => json({ title: 'Метки «фандинг» нет.' }, 404),
    });
    renderApp('/scoring');
    await screen.findByRole('table', { name: 'Веса меток' });
    const before = queriesOf(requests, '/api/scoring/labels').length;

    await type('Вес шорта фандинг', '2{Enter}');

    const row = screen.getByRole('row', { name: 'фандинг' });
    expect(await within(row).findByRole('alert')).toHaveTextContent(
      'Вес не сохранён: такой метки больше нет. Таблица перечитана.',
    );
    await waitFor(() =>
      expect(queriesOf(requests, '/api/scoring/labels').length).toBeGreaterThan(before),
    );
    expect(within(row).getByRole('textbox', { name: 'Вес шорта фандинг' })).toHaveValue('0');
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

  // Change decimal-label-weights, task 4.4: a row is a group of scores by its bounds.
  it('shows each group of scores with its ideas, the share in the plus, the mean and the sum, over 30 days at first', async () => {
    const requests = routes();
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Итоги по баллу' }));
    expect(
      table
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.getAttribute('aria-label')),
    ).toEqual(['Балл от +2 до +2.5', 'Балл от +0.5 до +1']);
    expect(
      within(table.getByRole('row', { name: 'Балл от +2 до +2.5' }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['от +2 до +2.5', '40', '50 %', '+0.20 %', '+1.50']);
    expect(queriesOf(requests, '/api/ideas/scores').at(-1)!.get('period')).toBe('30');
    expect(screen.getByText('52 идей')).toBeInTheDocument();
  });

  // Scenario «Малая выборка».
  it('marks a group of scores with fewer than 30 ideas as a small sample', async () => {
    routes({
      'GET /api/ideas/scores': () =>
        scores([
          [3, 12],
          [2, 40],
          [-0.5, 3],
        ]),
    });
    renderApp('/scoring');

    const table = within(await screen.findByRole('table', { name: 'Итоги по баллу' }));
    expect(
      within(table.getByRole('row', { name: 'Балл от +3 до +3.5' })).getByText('мало идей < 30'),
    ).toBeInTheDocument();
    expect(
      within(table.getByRole('row', { name: 'Балл от -0.5 до 0' })).getByText('мало идей < 30'),
    ).toBeInTheDocument();
    expect(
      within(table.getByRole('row', { name: 'Балл от +2 до +2.5' })).queryByText(/мало идей/),
    ).toBeNull();
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
