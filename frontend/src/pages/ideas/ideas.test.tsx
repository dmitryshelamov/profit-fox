import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { filterFields, ideaOf, ideasPage, savedFilterOf } from '@/test/fixtures';
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

function open(overrides: Record<string, (request: Request) => Response> = {}) {
  return fakeApi({
    'GET /api/ideas': () => json(ideasPage([wifIdea, fewData, noBucket])),
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
        json(
          ideasPage([
            ideaOf({ id: 'near', symbol: 'NEARUSDT', profitPerUsdt: '0.02', lossPerUsdt: '0.01' }),
            ideaOf({ id: 'far', symbol: 'FARUSDT', profitPerUsdt: '0.06', lossPerUsdt: '0.03' }),
          ]),
        ),
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

  const lastIdeasQuery = (requests: Request[]) => {
    const last = requests
      .filter((request) => new URL(request.url).pathname === '/api/ideas')
      .at(-1)!;

    return Object.fromEntries(new URL(last.url).searchParams);
  };
  const ideasRequests = (requests: Request[]) =>
    requests.filter((request) => new URL(request.url).pathname === '/api/ideas');

  it('asks the API for the first page of 50 by the expectancy, without a filter', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    expect(lastIdeasQuery(requests)).toEqual({
      Sort: 'expectancy',
      Direction: 'desc',
      Page: '1',
      PageSize: '50',
    });
    expect(ideasRequests(requests)).toHaveLength(1);
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

    await waitFor(() =>
      expect(lastIdeasQuery(requests)).toEqual({
        Setup: 'drop',
        Interval: '60',
        Type: 'pullback',
        Side: 'long',
        Category: 'linear',
        NonNegativeExpectancy: 'true',
        Sort: 'expectancy',
        Direction: 'desc',
        Page: '1',
        PageSize: '50',
      }),
    );
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
        json(ideasPage([ideaOf({ entry: '184.7', stop: '180', target: '197.00', tick: '0.01' })])),
    });
    renderApp('/ideas');

    const row = await rowOf('WIFUSDT');

    expect(within(row).getByText('184.70')).toBeInTheDocument();
    expect(within(row).getByText('180.00')).toBeInTheDocument();
    expect(within(row).getByText('197.00')).toBeInTheDocument();
  });

  // Scenario "Сортировка по R:R" and "Сортировка по всем страницам": the API sorts, the screen asks and shows the order it got.
  it('asks the API to sort by a column on a click on its header and the other way on the second click', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');
    expect(header(/Ожидание/)).toHaveAttribute('aria-sort', 'descending');
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'none');

    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    await waitFor(() =>
      expect(lastIdeasQuery(requests)).toMatchObject({ Sort: 'riskReward', Direction: 'desc' }),
    );
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'descending');
    expect(header(/R:R/)).toHaveTextContent('R:R ↓');
    expect(header(/Ожидание/)).toHaveAttribute('aria-sort', 'none');
    expect(header(/Ожидание/)).toHaveTextContent(/^Ожидание$/);

    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    await waitFor(() =>
      expect(lastIdeasQuery(requests)).toMatchObject({ Sort: 'riskReward', Direction: 'asc' }),
    );
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'ascending');
    expect(header(/R:R/)).toHaveTextContent('R:R ↑');
  });

  it('shows the ideas in the order the API gave', async () => {
    open({
      'GET /api/ideas': () =>
        json(
          ideasPage([
            ideaOf({ id: 'c', symbol: 'CCCUSDT' }),
            ideaOf({ id: 'a', symbol: 'AAAUSDT' }),
            ideaOf({ id: 'b', symbol: 'BBBUSDT' }),
          ]),
        ),
    });
    renderApp('/ideas');
    await rowOf('AAAUSDT');

    expect(symbolsInTable()).toEqual(['CCCUSDT', 'AAAUSDT', 'BBBUSDT']);
  });

  it('keeps the sorting when the list is read again with other filters', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');
    await userEvent.click(within(header(/R:R/)).getByRole('button'));

    await userEvent.click(screen.getByRole('checkbox', { name: 'Ожидание ≥ 0 R' }));

    await waitFor(() =>
      expect(lastIdeasQuery(requests)).toMatchObject({
        NonNegativeExpectancy: 'true',
        Sort: 'riskReward',
        Direction: 'desc',
      }),
    );
    expect(header(/R:R/)).toHaveAttribute('aria-sort', 'descending');
  });

  // Scenario "Возврат на «Идеи»".
  it('comes back with the filters, the switch, the sorting and the page size that were chosen', async () => {
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
    expect(lastIdeasQuery(requests)).toEqual({
      Interval: '60',
      Side: 'short',
      NonNegativeExpectancy: 'true',
      Sort: 'riskReward',
      Direction: 'desc',
      Page: '1',
      PageSize: '50',
    });
  });

  // Scenario "Сохранённый фильтр в запросе": the API applies it, the screen only names it.
  it('asks the API for the ideas that meet the saved filter that is chosen', async () => {
    const requests = open();
    renderApp('/ideas');
    await rowOf('WIFUSDT');

    await userEvent.click(await screen.findByRole('button', { name: 'Вероятные' }));

    await waitFor(() => expect(lastIdeasQuery(requests)).toMatchObject({ SavedFilter: 'big' }));
    expect(screen.getByLabelText('Условия фильтра')).toHaveTextContent('Вероятность > 45 %');
  });

  it('opens with the saved filter that was chosen', async () => {
    localStorage.setItem('profit-fox.ideas.choice', JSON.stringify({ savedFilterId: 'big' }));
    const requests = open();
    renderApp('/ideas');

    await rowOf('WIFUSDT');

    expect(await screen.findByRole('button', { name: 'Вероятные' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(lastIdeasQuery(requests)).toMatchObject({ SavedFilter: 'big' });
  });

  // Scenario "Удалённый фильтр": the ideas wait for the list of filters, so a filter that is gone is never asked for.
  it('opens without a saved filter that was deleted since', async () => {
    localStorage.setItem('profit-fox.ideas.choice', JSON.stringify({ savedFilterId: 'gone' }));
    const requests = open();
    renderApp('/ideas');

    await rowOf('WIFUSDT');

    expect(await screen.findByRole('button', { name: 'Вероятные' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(
      ideasRequests(requests).filter((request) =>
        new URL(request.url).searchParams.has('SavedFilter'),
      ),
    ).toHaveLength(0);
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
    open({ 'GET /api/ideas': () => json(ideasPage([])) });
    renderApp('/ideas');

    expect(await screen.findByText('Идей по этим условиям нет.')).toBeInTheDocument();
  });

  it('asks for the ideas again when the saved filter that is chosen is changed', async () => {
    localStorage.setItem('profit-fox.ideas.choice', JSON.stringify({ savedFilterId: 'big' }));
    const requests = open({
      'PUT /api/saved-filters/big': () => new Response(null, { status: 204 }),
    });
    renderApp('/ideas');
    await rowOf('WIFUSDT');
    await userEvent.click(await screen.findByRole('button', { name: 'Изменить' }));
    const before = ideasRequests(requests).length;

    await userEvent.click(
      within(screen.getByRole('dialog', { name: 'Редактор фильтра' })).getByRole('button', {
        name: 'Сохранить',
      }),
    );

    await waitFor(() => expect(ideasRequests(requests)).toHaveLength(before + 1));
  });

  describe('the pages', () => {
    // 120 ideas, the page the request names; the ideas of a page are named after their number.
    const pagedApi = (total = 120) => {
      const answer = (request: Request) => {
        const query = new URL(request.url).searchParams;
        const size = Number(query.get('PageSize') ?? 50);
        const last = Math.max(1, Math.ceil(total / size));
        const page = Math.min(Number(query.get('Page') ?? 1), last);
        const from = (page - 1) * size;
        const items = Array.from({ length: Math.min(size, total - from) }, (_, index) =>
          ideaOf({ id: `i${from + index + 1}`, symbol: `P${from + index + 1}USDT` }),
        );

        return json(ideasPage(items, { page, pageSize: size, total }));
      };

      return open({ 'GET /api/ideas': answer });
    };

    // Scenario "Вторая страница".
    it('shows the rows of the page and turns it with ›, writing the number into the address', async () => {
      const requests = pagedApi();
      const { history } = renderApp('/ideas');
      await rowOf('P1USDT');
      expect(screen.getByText('1–50 из 120')).toBeInTheDocument();
      expect(screen.getByText('120 идей')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Следующая страница' }));

      expect(await screen.findByText('51–100 из 120')).toBeInTheDocument();
      await rowOf('P51USDT');
      expect(lastIdeasQuery(requests)).toMatchObject({ Page: '2', PageSize: '50' });
      expect(history.location.search).toBe('?page=2');

      await userEvent.click(screen.getByRole('button', { name: 'Предыдущая страница' }));

      expect(await screen.findByText('1–50 из 120')).toBeInTheDocument();
      expect(history.location.search).toBe('');
    });

    it('opens the page the address names', async () => {
      const requests = pagedApi();
      renderApp('/ideas?page=3');

      expect(await screen.findByText('101–120 из 120')).toBeInTheDocument();
      expect(lastIdeasQuery(requests)).toMatchObject({ Page: '3' });
    });

    // Scenario "Смена фильтра на третьей странице".
    it.each([
      [
        'a filter',
        () => userEvent.selectOptions(screen.getByRole('combobox', { name: 'Сторона' }), 'short'),
        { Side: 'short' },
      ],
      [
        'the sorting',
        () => userEvent.click(within(header(/R:R/)).getByRole('button')),
        { Sort: 'riskReward' },
      ],
      [
        'the size of a page',
        () => userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '100'),
        { PageSize: '100' },
      ],
    ])('goes back to the first page on a change of %s', async (_name, change, expected) => {
      const requests = pagedApi();
      const { history } = renderApp('/ideas?page=3');
      await screen.findByText('101–120 из 120');

      await change();

      await waitFor(() =>
        expect(lastIdeasQuery(requests)).toMatchObject({ ...expected, Page: '1' }),
      );
      expect(history.location.search).toBe('');
    });

    it('goes back to the first page when a saved filter is chosen', async () => {
      const requests = pagedApi();
      const { history } = renderApp('/ideas?page=3');
      await screen.findByText('101–120 из 120');

      await userEvent.click(await screen.findByRole('button', { name: 'Вероятные' }));

      await waitFor(() =>
        expect(lastIdeasQuery(requests)).toMatchObject({ SavedFilter: 'big', Page: '1' }),
      );
      expect(history.location.search).toBe('');
    });

    // Scenario "Идей стало меньше".
    it('moves the address to the page the API gave when the one asked for is past the end', async () => {
      pagedApi(80);
      const { history } = renderApp('/ideas?page=3');

      expect(await screen.findByText('51–80 из 80')).toBeInTheDocument();
      await waitFor(() => expect(history.location.search).toBe('?page=2'));
    });

    // Scenario "Размер страницы".
    it('remembers the size of a page in the browser', async () => {
      const requests = pagedApi();
      const first = renderApp('/ideas');
      await rowOf('P1USDT');
      await userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '200');
      await screen.findByText('1–120 из 120');
      first.unmount();
      requests.length = 0;

      renderApp('/ideas');

      expect(await screen.findByText('1–120 из 120')).toBeInTheDocument();
      expect(lastIdeasQuery(requests)).toMatchObject({ PageSize: '200' });
      expect(screen.getByRole('combobox', { name: 'На странице' })).toHaveValue('200');
    });

    // Scenario "Назад со страницы пары".
    it('comes back to the page it left with Back from the pair', async () => {
      pagedApi();
      const { history } = renderApp('/ideas?page=2');
      await rowOf('P51USDT');

      await userEvent.click(within(await rowOf('P51USDT')).getByText('Откат'));
      await waitFor(() => expect(history.location.pathname).toBe('/market/linear/P51USDT'));
      history.back();

      expect(await screen.findByText('51–100 из 120')).toBeInTheDocument();
      expect(history.location.search).toBe('?page=2');
    });
  });
});

describe('caption of the ideas', () => {
  // Requirement "Кнопка «Войти» на идее": the screen no longer says that nothing is traded from it.
  it('tells that an entry is made from the page of the pair', async () => {
    fakeApi({
      'GET /api/ideas': () => json({ items: [], page: 1, pageSize: 50, total: 0 }),
      'GET /api/saved-filters': () => json([]),
    });
    renderApp('/ideas');

    expect(await screen.findByText(/Войти по идее можно со страницы пары/)).toBeInTheDocument();
    expect(screen.queryByText(/Только просмотр/)).toBeNull();
  });
});
