import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { filterFields, marketRow, marketSnapshot, savedFilterOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const none = { '5': [], '15': [], '60': [], '240': [] };
const pair = (
  symbol: string,
  setups: Record<string, string[]>,
  change1h: string,
  category = 'linear',
) => marketRow({ symbol, category, change1h, setups: { ...none, ...setups } });

const rows = [
  pair('WIFUSDT', { '60': ['impulse'] }, '2'),
  pair('SOLUSDT', { '60': ['impulse'] }, '5'),
  pair('DOGEUSDT', { '15': ['drop'] }, '1'),
  pair('ETHUSDT', { '60': ['impulse'] }, '3', 'spot'),
];
const fast = savedFilterOf({
  id: 'fast',
  name: 'Быстрые',
  place: 'market',
  conditions: [{ field: 'change1h', operator: '>', value: '4', to: null }],
});

const open = () =>
  fakeApi({
    'GET /api/market': () => json(marketSnapshot(rows)),
    'GET /api/ideas': () => json([]),
    'GET /api/saved-filters': () => json([fast]),
    'GET /api/saved-filters/fields': () => json(filterFields),
  });

const symbols = () =>
  within(screen.getByRole('table'))
    .getAllByRole('link')
    .map((link) => link.textContent);
const group = (name: string) => within(screen.getByRole('group', { name }));

describe('the choice of the market screen', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Возврат на «Рынок»".
  it('comes back with the category, the setup chip, its interval and the sorting, but not the search', async () => {
    open();
    const first = renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(group('Категория').getByRole('button', { name: /^linear/ }));
    await userEvent.click(group('ТФ сетапа').getByRole('button', { name: '1 ч' }));
    await userEvent.click(group('Сетап').getByRole('button', { name: /^Импульс/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Изменение за 1ч' }));
    await userEvent.type(screen.getByRole('searchbox'), 'WIF');
    first.unmount();

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
    // The impulses of linear on 1 hour, the lowest growth first: the choice of the sorting came back too.
    expect(symbols()).toEqual(['WIFUSDT', 'SOLUSDT']);
  });

  it('opens with the saved filter that was chosen', async () => {
    localStorage.setItem('profit-fox.market.choice', JSON.stringify({ savedFilterId: 'fast' }));
    open();
    renderApp('/market');

    await screen.findByRole('table');

    expect(await screen.findByRole('button', { name: 'Быстрые' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(symbols()).toEqual(['SOLUSDT']);
  });

  // Scenario "Удалённый фильтр".
  it('opens without a saved filter that was deleted since', async () => {
    localStorage.setItem('profit-fox.market.choice', JSON.stringify({ savedFilterId: 'gone' }));
    open();
    renderApp('/market');

    await screen.findByRole('table');

    expect(await screen.findByRole('button', { name: 'Быстрые' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(symbols()).toHaveLength(4);
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
    expect(symbols()[0]).toBe('SOLUSDT');
  });
});
