import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { SavedFilter } from '@/api/filters';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { filterRoutes, highFunding, shorts } from '@/test/filters';
import { ideaPage, scannerState } from '@/test/ideas';
import { marketList, marketQueries } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

function routes(saved: SavedFilter[], extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/market/pairs': marketList(831),
    ...filterRoutes(saved),
    ...extra,
  });
}

const last = (requests: readonly Request[]) => marketQueries(requests).at(-1)!;
const row = async () => within(await screen.findByRole('group', { name: 'Фильтры' }));

// Spec saved-filters «Строка фильтров» (change add-ideas-and-market, task 15.1).
describe('the row of filters', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Выбор фильтра».
  it('chooses a filter: its badge is lit, its conditions are told and the table opens on its first page', async () => {
    const requests = routes([highFunding]);
    const { history } = renderApp('/market?page=2');
    await screen.findByRole('row', { name: 'P781USDT' });

    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));

    expect((await row()).getByRole('button', { name: 'Высокий фандинг' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByLabelText('Условия фильтра')).toHaveTextContent(
      'Фандинг, % ≥ 0.03 и Оборот 24 ч, млн USDT > 100',
    );
    await waitFor(() => expect(last(requests).get('filter')).toBe('f-high'));
    expect(last(requests).get('page')).toBe('1');
    expect(history.location.search).not.toContain('page=');
  });

  // Scenario «Снятие».
  it('takes the filter off by «Снять» and by its lit badge', async () => {
    const requests = routes([highFunding]);
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));
    await waitFor(() => expect(last(requests).get('filter')).toBe('f-high'));

    await userEvent.click(screen.getByRole('button', { name: 'Снять' }));
    await waitFor(() => expect(last(requests).get('filter')).toBeNull());
    expect((await row()).getByRole('button', { name: 'Высокий фандинг' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );

    await userEvent.click((await row()).getByRole('button', { name: 'Высокий фандинг' }));
    await userEvent.click((await row()).getByRole('button', { name: 'Высокий фандинг' }));
    await waitFor(() => expect(last(requests).get('filter')).toBeNull());
    expect(screen.queryByLabelText('Условия фильтра')).toBeNull();
  });

  // Scenario «Фильтр другого места».
  it('has no badge of a filter of «Идеи» in «Рынок»', async () => {
    routes([highFunding, shorts]);
    renderApp('/market');

    await (await row()).findByRole('button', { name: 'Высокий фандинг' });
    expect((await row()).queryByRole('button', { name: 'Шорты' })).toBeNull();
  });

  it('remembers the chosen filter of the place in the browser', async () => {
    routes([highFunding]);
    const first = renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));
    first.unmount();

    const again = routes([highFunding]);
    renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });
    expect(marketQueries(again)[0]!.get('filter')).toBe('f-high');
  });

  // Scenario «Фильтр удалили в другой вкладке».
  it('takes off a chosen filter that the list says is gone', async () => {
    localStorage.setItem('profit-fox.filter.market', JSON.stringify('f-gone'));
    const requests = routes([], {
      'GET /api/market/pairs': (request) => {
        const filter = new URL(request.url).searchParams.get('filter');
        return json({
          items: [],
          total: 831,
          page: 1,
          pageSize: 50,
          filterGone: filter !== null,
          updatedAt: Date.now(),
        });
      },
    });
    renderApp('/market');

    await waitFor(() => expect(last(requests).get('filter')).toBeNull());
    expect(marketQueries(requests)[0]!.get('filter')).toBe('f-gone');
    expect(localStorage.getItem('profit-fox.filter.market')).toBe('null');
  });
});

// Spec saved-filters «Редактор фильтра» (task 15.2).
describe('the editor of a filter', () => {
  afterEach(() => vi.unstubAllGlobals());

  const counter: FakeRoute = async (request) => {
    const body = (await request.json()) as { conditions: { value: string }[] };
    return json({
      pairs: body.conditions.length === 2 ? 12 : 40,
      allPairs: 831,
      ideas: 3,
      allIdeas: 9,
    });
  };

  // Scenarios «Новый фильтр» and «Счётчик».
  it('saves a new filter of «Рынок» with two conditions, counting the rows on each change, and chooses it', async () => {
    const saved: SavedFilter[] = [];
    const requests = routes(saved, {
      'POST /api/filters/count': counter,
      'POST /api/filters': async (request) => {
        const body = (await request.json()) as Omit<SavedFilter, 'id'>;
        const filter = { ...body, id: 'f-new' };
        saved.push(filter);
        return json(filter, 201);
      },
    });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: '+ Новый фильтр' }));
    const panel = within(screen.getByRole('dialog', { name: 'Фильтр' }));

    await userEvent.type(panel.getByLabelText('Название'), 'Высокий фандинг');
    expect(panel.getByRole('radio', { name: 'Рынок' })).toHaveAttribute('aria-checked', 'true');
    const first = within(panel.getByRole('group', { name: 'Условие 1' }));
    await userEvent.type(first.getByLabelText('Значение'), '0.03');
    await waitFor(() =>
      expect(panel.getByRole('status', { name: 'Подходит' })).toHaveTextContent(
        'Подходит: 40 из 831',
      ),
    );
    await userEvent.click(panel.getByRole('button', { name: '+ Условие' }));
    const second = within(panel.getByRole('group', { name: 'Условие 2' }));
    await userEvent.selectOptions(second.getByLabelText('Поле'), 'turnover24h');
    await userEvent.selectOptions(second.getByLabelText('Оператор'), 'gt');
    await userEvent.type(second.getByLabelText('Значение'), '100');
    await waitFor(() =>
      expect(panel.getByRole('status', { name: 'Подходит' })).toHaveTextContent(
        'Подходит: 12 из 831',
      ),
    );
    await userEvent.click(panel.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Фильтр' })).toBeNull());
    expect(await bodiesOf(requests, 'POST', '/api/filters')).toEqual([
      {
        name: 'Высокий фандинг',
        place: 'market',
        conditions: [
          { field: 'fundingRate', operator: 'ge', value: '0.03' },
          { field: 'turnover24h', operator: 'gt', value: '100', to: null },
        ],
      },
    ]);
    expect(await (await row()).findByRole('button', { name: 'Высокий фандинг' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await waitFor(() => expect(last(requests).get('filter')).toBe('f-new'));
  });

  // Scenario «Выбор поля».
  it('puts the fields in two groups, «Рынок» and «Идея», and a field with a set of values takes a value from the list', async () => {
    routes([], { 'POST /api/filters/count': counter });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: '+ Новый фильтр' }));
    const condition = within(
      within(screen.getByRole('dialog', { name: 'Фильтр' })).getByRole('group', {
        name: 'Условие 1',
      }),
    );

    const groups = within(condition.getByLabelText('Поле')).getAllByRole('group');
    expect(groups.map((group) => group.getAttribute('label'))).toEqual(['Рынок', 'Идея']);
    expect(
      within(groups[1]!)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['Стратегия', 'Таймфрейм', 'Сторона']);

    await userEvent.selectOptions(condition.getByLabelText('Поле'), 'side');

    expect(
      within(condition.getByLabelText('Значение'))
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['лонг', 'шорт']);
    expect(
      within(condition.getByLabelText('Оператор'))
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['=']);
  });

  // Scenario «Отказ в панели».
  it('keeps the panel open with the reason at the name when the name is taken', async () => {
    routes([highFunding], {
      'POST /api/filters/count': counter,
      'POST /api/filters': () =>
        json(
          {
            errors: [
              { code: 'TakenName', reason: 'фильтр «Высокий фандинг» уже есть', field: 'name' },
            ],
          },
          422,
        ),
    });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: '+ Новый фильтр' }));
    const panel = within(screen.getByRole('dialog', { name: 'Фильтр' }));
    await userEvent.type(panel.getByLabelText('Название'), 'Высокий фандинг');
    await userEvent.type(
      within(panel.getByRole('group', { name: 'Условие 1' })).getByLabelText('Значение'),
      '0.05',
    );

    await userEvent.click(panel.getByRole('button', { name: 'Сохранить' }));

    expect(await panel.findByRole('alert', { name: 'Ошибка названия' })).toHaveTextContent(
      'фильтр «Высокий фандинг» уже есть',
    );
    expect(screen.getByRole('dialog', { name: 'Фильтр' })).toBeInTheDocument();
  });

  it('tells the error of a condition at its number', async () => {
    routes([], {
      'POST /api/filters/count': counter,
      'POST /api/filters': () =>
        json(
          {
            errors: [
              {
                code: 'InvalidCondition',
                reason: 'значение — число',
                field: 'conditions',
                condition: 0,
              },
            ],
          },
          422,
        ),
    });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: '+ Новый фильтр' }));
    const panel = within(screen.getByRole('dialog', { name: 'Фильтр' }));
    await userEvent.type(panel.getByLabelText('Название'), 'Слово');
    await userEvent.type(
      within(panel.getByRole('group', { name: 'Условие 1' })).getByLabelText('Значение'),
      'abc',
    );

    await userEvent.click(panel.getByRole('button', { name: 'Сохранить' }));

    expect(
      await within(panel.getByRole('group', { name: 'Условие 1' })).findByRole('alert', {
        name: 'Ошибка условия 1',
      }),
    ).toHaveTextContent('значение — число');
  });

  // Scenario «Отмена».
  it('closes on «Отмена» after changes and leaves the filter as it was', async () => {
    const requests = routes([highFunding], { 'POST /api/filters/count': counter });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));
    await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));
    const panel = within(screen.getByRole('dialog', { name: 'Фильтр' }));
    expect(panel.getByLabelText('Название')).toHaveValue('Высокий фандинг');
    await userEvent.clear(panel.getByLabelText('Название'));
    await userEvent.type(panel.getByLabelText('Название'), 'Другое');

    await userEvent.click(panel.getByRole('button', { name: 'Отмена' }));

    expect(screen.queryByRole('dialog', { name: 'Фильтр' })).toBeNull();
    expect(
      requests.filter(
        (request) =>
          request.method === 'PUT' ||
          (request.method === 'POST' && new URL(request.url).pathname === '/api/filters'),
      ),
    ).toEqual([]);
    expect((await row()).getByRole('button', { name: 'Высокий фандинг' })).toBeInTheDocument();
  });

  it('deletes a saved filter and takes it off', async () => {
    const saved = [highFunding];
    const requests = routes(saved, {
      'POST /api/filters/count': counter,
      'DELETE /api/filters/f-high': () => {
        saved.length = 0;
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));
    await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));

    await userEvent.click(screen.getByRole('button', { name: 'Удалить фильтр' }));

    const filters = await row();
    await waitFor(() =>
      expect(filters.queryByRole('button', { name: 'Высокий фандинг' })).toBeNull(),
    );
    await waitFor(() => expect(last(requests).get('filter')).toBeNull());
  });
});

// Task 18.21: saving and deleting the chosen filter read the lists of its places again.
describe('the lists after a change of their filter', () => {
  const ideaRoutes = (saved: SavedFilter[], extra: Record<string, FakeRoute> = {}) =>
    fakeApi({
      ...baseRoutes(),
      'GET /api/strategies': () => json([]),
      'GET /api/ideas/state': () => json(scannerState()),
      'GET /api/ideas': ideaPage([]),
      'GET /api/ideas/summary': () =>
        json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
      'POST /api/filters/count': () => json({ pairs: 3, ideas: 5 }),
      ...filterRoutes(saved),
      ...extra,
    });
  const lists = (requests: readonly Request[]) =>
    requests.filter((request) => {
      const path = new URL(request.url).pathname;
      return request.method === 'GET' && (path === '/api/ideas' || path === '/api/ideas/summary');
    }).length;

  it('reads the journal again after «Изменить» → «Сохранить» of the chosen filter', async () => {
    const saved = [shorts];
    const requests = ideaRoutes(saved, {
      'PUT /api/filters/f-shorts': async (request) => {
        const body = (await request.json()) as Omit<SavedFilter, 'id'>;
        saved[0] = { ...shorts, ...body };
        return json(saved[0]);
      },
    });
    renderApp('/ideas?tab=journal');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Шорты' }));
    await waitFor(() => expect(lists(requests)).toBeGreaterThan(0));
    await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));
    const panel = within(screen.getByRole('dialog', { name: 'Фильтр' }));
    await userEvent.clear(panel.getByLabelText('Название'));
    await userEvent.type(panel.getByLabelText('Название'), 'Шорты v2');
    const before = lists(requests);

    await userEvent.click(panel.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(lists(requests)).toBeGreaterThanOrEqual(before + 2));
  });

  it('reads the market again after the chosen filter is deleted', async () => {
    const saved = [highFunding];
    const requests = routes(saved, {
      'POST /api/filters/count': () => json({ pairs: 3, ideas: null }),
      'DELETE /api/filters/f-high': () => {
        saved.length = 0;
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/market');
    await userEvent.click(await (await row()).findByRole('button', { name: 'Высокий фандинг' }));
    await waitFor(() => expect(last(requests).get('filter')).toBe('f-high'));
    await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));
    const before = marketQueries(requests).length;

    await userEvent.click(screen.getByRole('button', { name: 'Удалить фильтр' }));

    await waitFor(() => expect(marketQueries(requests).length).toBeGreaterThan(before));
  });
});
