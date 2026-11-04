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
      'Время',
      'Инструмент',
      'Сетап',
      'ТФ',
      'Тип',
      'Сторона',
      'Вход',
      'Стоп',
      'Цель',
      'R:R',
      'Вероятность',
      'Ожидание',
      'Прибыль',
      'Убыток',
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
    expect(within(row).getByText('Long')).toHaveClass('text-ok');
    expect(within(row).getByText('2.40')).toBeInTheDocument();
    expect(within(row).getByText('2.00')).toBeInTheDocument();
    expect(within(row).getByText('+0.38 R')).toBeInTheDocument();
    expect(within(row).getByText('Ждёт входа')).toBeInTheDocument();
    expect(await screen.findByText('3 идеи')).toBeInTheDocument();
  });

  // Scenario "Идея выше базовой линии".
  it('shows the probability with N and ▲ when the interval is above the baseline', async () => {
    open();
    renderApp('/ideas');

    const row = await rowOf('WIFUSDT');

    expect(within(row).getByText(/^46 %/)).toBeInTheDocument();
    expect(within(row).getByText('N 120')).toBeInTheDocument();
    expect(within(row).getByRole('img', { name: 'выше базовой линии' })).toHaveTextContent('▲');
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
    // No expectancy either, and no risk typed yet: three dashes.
    expect(within(none).getAllByText('—')).toHaveLength(3);
  });

  // Scenario "Риск на идею".
  it('turns the risk per idea into the profit and the loss and remembers it in the browser', async () => {
    open();
    renderApp('/ideas');
    const row = await rowOf('WIFUSDT');

    expect(within(row).getAllByText('—')).toHaveLength(2);
    await userEvent.type(screen.getByRole('textbox', { name: 'Риск на идею' }), '10');

    expect(within(row).getByText('+20.0')).toBeInTheDocument();
    expect(within(row).getByText('−10.0')).toBeInTheDocument();
    expect(within(await rowOf('SOLUSDT')).getByText('+15.0')).toBeInTheDocument();
    expect(localStorage.getItem('profit-fox.ideas.risk')).toBe('10');
  });

  it('starts with the risk it remembers', async () => {
    localStorage.setItem('profit-fox.ideas.risk', '25');
    open();
    renderApp('/ideas');

    expect(within(await rowOf('WIFUSDT')).getByText('+50.0')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Риск на идею' })).toHaveValue('25');
  });

  it('asks the API for the ideas the filters name', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сетап' }), 'drop');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'ТФ' }), '60');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Тип' }), 'pullback');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'long');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Категория' }), 'linear');
    await userEvent.click(screen.getByRole('switch', { name: 'Ожидание ≥ 0 R' }));

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
