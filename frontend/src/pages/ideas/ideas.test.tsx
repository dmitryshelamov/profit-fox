import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { filterFields, ideaOf, marketRow, marketSnapshot, savedFilterOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const wifIdea = ideaOf({ id: 'wif-1', symbol: 'WIFUSDT' });
const fewData = ideaOf({
  id: 'sol-1',
  symbol: 'SOLUSDT',
  side: 'short',
  type: 'levels',
  status: 'stop',
  interval: '60',
  riskReward: '1.5',
  profitPerUsdt: '0.02',
  lossPerUsdt: '0.01',
  bucket: {
    cases: 12,
    expired: 0,
    probability: null,
    probabilityLow: null,
    probabilityHigh: null,
    baseline: '0.4',
    expectancy: '-0.1',
    enoughData: false,
  },
});
const noBucket = ideaOf({ id: 'doge-1', symbol: 'DOGEUSDT', bucket: null });

const bigFilter = savedFilterOf({
  id: 'big',
  name: 'Вероятные',
  place: 'ideas',
  conditions: [{ field: 'probability', operator: '>', value: '0.45', to: null }],
});

function open(overrides: Record<string, () => Response> = {}) {
  return fakeApi({
    'GET /api/ideas': () => json([wifIdea, fewData, noBucket]),
    'GET /api/market': () => json(marketSnapshot([marketRow({ symbol: 'WIFUSDT' })])),
    'GET /api/saved-filters': () => json([bigFilter]),
    'GET /api/saved-filters/fields': () => json(filterFields),
    ...overrides,
  });
}

const rowOf = async (symbol: string) =>
  (await screen.findByRole('link', { name: symbol })).closest('tr')!;

describe('ideas screen', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('lists the ideas with their columns in the order the API gave', async () => {
    open();
    renderApp('/ideas');

    const row = await rowOf('WIFUSDT');

    expect(
      within(screen.getByRole('table'))
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual([
      'Найдена',
      'Инструмент',
      'Сетап',
      'ТФ',
      'Тип идеи',
      'Сторона',
      'Вход',
      'Стоп',
      'Цель',
      'R:R',
      'Вероятность',
      'Ожидание ↓',
      'Прибыль / убыток',
      'Статус',
    ]);
    expect(
      within(screen.getByRole('table'))
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['WIFUSDT', 'SOLUSDT', 'DOGEUSDT']);
    expect(within(row).getByText('04.11 11:45')).toBeInTheDocument();
    expect(within(row).getByText('Импульс')).toBeInTheDocument();
    expect(within(row).getByText('15 мин')).toBeInTheDocument();
    expect(within(row).getByText('Откат')).toBeInTheDocument();
    expect(within(row).getByText('Лонг')).toHaveClass('text-ok');
    expect(within(row).getByText('2.40')).toBeInTheDocument();
    expect(within(row).getByText('2.00')).toBeInTheDocument();
    expect(within(row).getByText('+0.38 R')).toBeInTheDocument();
    expect(within(row).getByText('+0.39')).toBeInTheDocument();
    expect(within(row).getByText('−0.21')).toBeInTheDocument();
    expect(within(row).getByText('Ждёт входа')).toBeInTheDocument();
    expect(await screen.findByText('3 идеи')).toBeInTheDocument();
    expect(
      screen.getByRole('group', { name: 'Категория' }).querySelectorAll('button'),
    ).toHaveLength(3);
  });

  it('explains the probability, the badges and the profit under the table', async () => {
    open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    expect(screen.getByText(/Вероятность: доля «Цель» среди «Цель» и «Стоп»/)).toBeInTheDocument();
    expect(screen.getByText(/интервал вероятности выше, пересекает или ниже/)).toBeInTheDocument();
    expect(
      screen.getByText(/на сумму на идею: при цели и при стопе, после комиссий/),
    ).toBeInTheDocument();
  });

  // Scenario "Идея выше базовой линии".
  it('shows the probability with N, and ▲ before the expectancy when the interval is above the baseline', async () => {
    open();
    renderApp('/ideas');

    const row = await rowOf('WIFUSDT');

    expect(within(row).getByText(/^46 %/)).toBeInTheDocument();
    expect(within(row).getByText('· 120')).toBeInTheDocument();
    const badge = within(row).getByRole('img', { name: 'выше базовой линии' });
    expect(badge).toHaveTextContent('▲');
    expect(badge.parentElement).toHaveTextContent('▲+0.38 R');
  });

  // Scenario "Мало данных".
  it('says «мало данных» without a badge when N is below 30 or the bucket has no case', async () => {
    open();
    renderApp('/ideas');

    const few = await rowOf('SOLUSDT');
    const none = await rowOf('DOGEUSDT');

    expect(within(few).getByText('мало данных')).toBeInTheDocument();
    expect(within(few).queryByRole('img')).not.toBeInTheDocument();
    expect(within(none).getByText('мало данных')).toBeInTheDocument();
    // No expectancy either: one dash. The profit and the loss come from the amount, which does not need the bucket.
    expect(within(none).getAllByText('—')).toHaveLength(1);
    expect(within(none).getByText('+0.39')).toBeInTheDocument();
  });

  // Scenario "Сумма на идею".
  it('turns the amount per idea into the profit and the loss and remembers it in the browser', async () => {
    open();
    renderApp('/ideas');
    const row = await rowOf('WIFUSDT');

    const amount = screen.getByRole('textbox', { name: 'Сумма на идею' });

    expect(amount).toHaveValue('10');
    expect(screen.getAllByText('USDT')).toHaveLength(1);
    expect(within(row).getByText('+0.39')).toBeInTheDocument();
    await userEvent.clear(amount);
    expect(within(row).getByText('—')).toBeInTheDocument();
    await userEvent.type(amount, '30');

    expect(within(row).getByText('+1.17')).toBeInTheDocument();
    expect(within(row).getByText('−0.63')).toBeInTheDocument();
    expect(within(await rowOf('SOLUSDT')).getByText('+0.60')).toBeInTheDocument();
    expect(localStorage.getItem('profit-fox.ideas.amount')).toBe('30');
  });

  // Scenario "Цели 2R дают разную прибыль".
  it('gives the money by the distance to the stop: a farther stop gives more', async () => {
    open({
      'GET /api/ideas': () =>
        json([
          ideaOf({ id: 'near', symbol: 'NEARUSDT', profitPerUsdt: '0.02', lossPerUsdt: '0.01' }),
          ideaOf({ id: 'far', symbol: 'FARUSDT', profitPerUsdt: '0.06', lossPerUsdt: '0.03' }),
        ]),
    });
    renderApp('/ideas');

    const near = within(await rowOf('NEARUSDT'));
    const far = within(await rowOf('FARUSDT'));

    expect([near.getByText('+0.20'), near.getByText('−0.10')]).toHaveLength(2);
    expect([far.getByText('+0.60'), far.getByText('−0.30')]).toHaveLength(2);
  });

  it('starts with the amount it remembers', async () => {
    localStorage.setItem('profit-fox.ideas.amount', '25');
    open();
    renderApp('/ideas');

    expect(within(await rowOf('WIFUSDT')).getByText('+0.97')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Сумма на идею' })).toHaveValue('25');
  });

  it('asks the API for the ideas the filters name', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сетап' }), 'drop');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'ТФ' }), '60');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Тип идеи' }), 'pullback');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'long');
    await userEvent.click(
      within(screen.getByRole('group', { name: 'Категория' })).getByRole('button', {
        name: 'linear',
      }),
    );
    await userEvent.click(screen.getByRole('checkbox', { name: 'Ожидание ≥ 0 R' }));

    await waitFor(() => {
      const last = requests
        .filter((request) => new URL(request.url).pathname === '/api/ideas')
        .at(-1)!;
      expect(Object.fromEntries(new URL(last.url).searchParams)).toEqual({
        Setup: 'drop',
        Interval: '60',
        Type: 'pullback',
        Side: 'long',
        Category: 'linear',
        NonNegativeExpectancy: 'true',
      });
    });
  });

  const symbolsInTable = () =>
    within(screen.getByRole('table'))
      .getAllByRole('link')
      .map((link) => link.textContent);
  const header = (name: RegExp) => screen.getByRole('columnheader', { name });

  // Scenario "Вход по рынку и лимитный вход".
  it('shows the entry, the stop and the target by the price step of the pair', async () => {
    open({
      'GET /api/ideas': () =>
        json([ideaOf({ entry: '184.7', stop: '180', target: '197.00', tick: '0.01' })]),
    });
    renderApp('/ideas');

    const row = await rowOf('WIFUSDT');

    expect(within(row).getByText('184.70')).toBeInTheDocument();
    expect(within(row).getByText('180.00')).toBeInTheDocument();
    expect(within(row).getByText('197.00')).toBeInTheDocument();
  });

  // Scenario "Сортировка по R:R".
  it('sorts by a column on a click on its header and the other way on the second click', async () => {
    open({
      'GET /api/ideas': () =>
        json([
          ideaOf({ id: 'a', symbol: 'AAAUSDT', riskReward: '2' }),
          ideaOf({ id: 'b', symbol: 'BBBUSDT', riskReward: '1.5' }),
          ideaOf({ id: 'c', symbol: 'CCCUSDT', riskReward: '3' }),
        ]),
    });
    renderApp('/ideas');
    await rowOf('AAAUSDT');
    expect(header(/Ожидание/)).toHaveAttribute('aria-sort', 'descending');
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'none');

    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    expect(symbolsInTable()).toEqual(['CCCUSDT', 'AAAUSDT', 'BBBUSDT']);
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'descending');
    expect(header(/R:R/)).toHaveTextContent('R:R ↓');
    expect(header(/Ожидание/)).toHaveAttribute('aria-sort', 'none');
    expect(header(/Ожидание/)).toHaveTextContent(/^Ожидание$/);

    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    expect(symbolsInTable()).toEqual(['BBBUSDT', 'AAAUSDT', 'CCCUSDT']);
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'ascending');
    expect(header(/R:R/)).toHaveTextContent('R:R ↑');
  });

  // Scenario "Вероятность без данных".
  it('keeps the ideas without a probability under the others in both directions', async () => {
    const withData = (probability: string) => ({ ...ideaOf().bucket!, probability });
    open({
      'GET /api/ideas': () =>
        json([
          fewData,
          noBucket,
          ideaOf({ id: 'p1', symbol: 'LOWUSDT', bucket: withData('0.46') }),
          ideaOf({ id: 'p2', symbol: 'HIGHUSDT', bucket: withData('0.5') }),
        ]),
    });
    renderApp('/ideas');
    await rowOf('LOWUSDT');

    await userEvent.click(within(header(/Вероятность/)).getByRole('button'));
    expect(symbolsInTable()).toEqual(['HIGHUSDT', 'LOWUSDT', 'SOLUSDT', 'DOGEUSDT']);

    await userEvent.click(within(header(/Вероятность/)).getByRole('button'));
    expect(symbolsInTable()).toEqual(['LOWUSDT', 'HIGHUSDT', 'SOLUSDT', 'DOGEUSDT']);
  });

  it('keeps the sorting when the list is read again with other filters', async () => {
    const ideas = [
      ideaOf({ id: 'a', symbol: 'AAAUSDT', riskReward: '2' }),
      ideaOf({ id: 'b', symbol: 'BBBUSDT', riskReward: '3' }),
    ];
    const requests = open({ 'GET /api/ideas': () => json(ideas) });
    renderApp('/ideas');
    await rowOf('AAAUSDT');
    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    await userEvent.click(screen.getByRole('checkbox', { name: 'Ожидание ≥ 0 R' }));

    await waitFor(() =>
      expect(
        requests.some((request) => new URL(request.url).searchParams.has('NonNegativeExpectancy')),
      ).toBe(true),
    );
    expect(symbolsInTable()).toEqual(['BBBUSDT', 'AAAUSDT']);
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'descending');
  });

  // Scenario "Возврат на «Идеи»".
  it('comes back with the filters, the switch and the sorting that were chosen', async () => {
    const requests = open();
    const first = renderApp('/ideas');
    await rowOf('WIFUSDT');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'ТФ' }), '60');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'short');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Ожидание ≥ 0 R' }));
    await userEvent.click(within(header(/R:R/)).getByRole('button'));
    first.unmount();
    requests.length = 0;

    renderApp('/ideas');
    await rowOf('WIFUSDT');

    expect(screen.getByRole('combobox', { name: 'ТФ' })).toHaveValue('60');
    expect(screen.getByRole('combobox', { name: 'Сторона' })).toHaveValue('short');
    expect(screen.getByRole('checkbox', { name: 'Ожидание ≥ 0 R' })).toBeChecked();
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'descending');
    expect(
      Object.fromEntries(
        new URL(requests.find((request) => new URL(request.url).searchParams.has('Side'))!.url)
          .searchParams,
      ),
    ).toEqual({ Interval: '60', Side: 'short', NonNegativeExpectancy: 'true' });
  });

  it('opens with the saved filter that was chosen', async () => {
    localStorage.setItem('profit-fox.ideas.choice', JSON.stringify({ savedFilterId: 'big' }));
    open();
    renderApp('/ideas');

    await rowOf('WIFUSDT');

    expect(await screen.findByRole('button', { name: 'Вероятные' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(symbolsInTable()).toEqual(['WIFUSDT']);
  });

  // Scenario "Удалённый фильтр".
  it('opens without a saved filter that was deleted since', async () => {
    localStorage.setItem('profit-fox.ideas.choice', JSON.stringify({ savedFilterId: 'gone' }));
    open();
    renderApp('/ideas');

    await rowOf('WIFUSDT');

    expect(await screen.findByRole('button', { name: 'Вероятные' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(symbolsInTable()).toEqual(['WIFUSDT', 'SOLUSDT', 'DOGEUSDT']);
  });

  it('opens as before when what is stored is broken', async () => {
    localStorage.setItem('profit-fox.ideas.choice', '{broken');
    open();
    renderApp('/ideas');

    await rowOf('WIFUSDT');

    expect(screen.getByRole('combobox', { name: 'ТФ' })).toHaveValue('');
    expect(header(/Ожидание/)).toHaveAttribute('aria-sort', 'descending');
  });

  it('opens the pair with the idea selected when a row is clicked', async () => {
    open();
    const { history } = renderApp('/ideas');

    await userEvent.click(within(await rowOf('WIFUSDT')).getByText('Откат'));

    await waitFor(() => expect(history.location.pathname).toBe('/market/linear/WIFUSDT'));
    expect(history.location.search).toBe('?idea=wif-1');
  });

  it('says that there are no ideas for the filters', async () => {
    open({ 'GET /api/ideas': () => json([]) });
    renderApp('/ideas');

    expect(await screen.findByText('Идей по этим условиям нет.')).toBeInTheDocument();
  });

  it('applies a saved filter of the ideas to the list', async () => {
    open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    await userEvent.click(await screen.findByRole('button', { name: 'Вероятные' }));

    expect(
      within(screen.getByRole('table'))
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['WIFUSDT']);
    expect(screen.getByLabelText('Условия фильтра')).toHaveTextContent('Вероятность > 45 %');
  });
});
