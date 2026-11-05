import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { filterFields, marketRow, marketSnapshot, savedFilterOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const open = () =>
  fakeApi({
    'GET /api/market': () => json(marketSnapshot([marketRow({ symbol: 'WIFUSDT' })])),
    'GET /api/saved-filters': () => json([fast]),
    'GET /api/saved-filters/fields': () => json(filterFields),
  });

const fast = savedFilterOf({
  id: 'fast',
  name: 'Быстрые',
  place: 'market',
  conditions: [{ field: 'change1h', operator: '>', value: '4', to: null }],
});

const lastRead = (requests: Request[]) =>
  Object.fromEntries(
    new URL(
      requests.filter((request) => new URL(request.url).pathname === '/api/market').at(-1)!.url,
    ).searchParams,
  );
const group = (name: string) => within(screen.getByRole('group', { name }));

describe('the choice of the market screen', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Возврат на «Рынок»".
  it('comes back with the category, the setup chip, its interval, the sorting and the size of a page, but not the search', async () => {
    open();
    const first = renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(group('Категория').getByRole('button', { name: /^linear/ }));
    await userEvent.click(group('ТФ сетапа').getByRole('button', { name: '1 ч' }));
    await userEvent.click(group('Сетап').getByRole('button', { name: /^Импульс/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Изменение за 1ч' }));
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '100');
    await userEvent.type(screen.getByRole('searchbox'), 'WIF');
    first.unmount();

    const requests = open();
    renderApp('/market');
    await screen.findByRole('table');

    expect(group('Категория').getByRole('button', { name: /^linear/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(group('ТФ сетапа').getByRole('button', { name: '1 ч' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(group('Сетап').getByRole('button', { name: /^Импульс/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('searchbox')).toHaveValue('');
    // The sorting came back too: the lowest growth first.
    expect(lastRead(requests)).toEqual({
      Category: 'linear',
      Setup: 'impulse',
      Interval: '60',
      Sort: 'change1h',
      Direction: 'asc',
      Page: '1',
      PageSize: '100',
    });
  });

  it('opens with the saved filter that was chosen', async () => {
    localStorage.setItem('profit-fox.market.choice', JSON.stringify({ savedFilterId: 'fast' }));
    const requests = open();
    renderApp('/market');

    await screen.findByRole('table');

    expect(await screen.findByRole('button', { name: 'Быстрые' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(lastRead(requests)).toMatchObject({ SavedFilter: 'fast' });
  });

  // Scenario "Удалённый фильтр": the table waits for the list of filters, so a filter that is gone is never asked for.
  it('opens without a saved filter that was deleted since', async () => {
    localStorage.setItem('profit-fox.market.choice', JSON.stringify({ savedFilterId: 'gone' }));
    const requests = open();
    renderApp('/market');

    await screen.findByRole('table');

    expect(await screen.findByRole('button', { name: 'Быстрые' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(requests.some((request) => new URL(request.url).searchParams.has('SavedFilter'))).toBe(
      false,
    );
  });

  it('opens as before when what is stored is broken', async () => {
    localStorage.setItem('profit-fox.market.choice', '{broken');
    open();
    renderApp('/market');

    await screen.findByRole('table');

    expect(group('Категория').getByRole('button', { name: /^Все/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Изменение за 1ч' }).closest('th')).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });
});
