import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { marketRow, marketSnapshot } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const none = { '5': [], '15': [], '60': [], '240': [] };
const pair = (symbol: string, setups: Record<string, string[]>, category = 'linear') =>
  marketRow({ symbol, category, setups: { ...none, ...setups } });

const wif = pair('WIFUSDT', { '15': ['impulse', 'high_volatility'] });
const sol = pair('SOLUSDT', { '15': ['impulse'], '5': ['drop'] });
const doge = pair('DOGEUSDT', { '15': ['drop'] });
const eth = pair('ETHUSDT', {}, 'spot');

const symbolsIn = (table: HTMLElement) =>
  within(table)
    .getAllByRole('link')
    .map((link) => link.textContent);

// What the API does with the query: the category, the search and the setup on the interval cut the rows; the chips count what is left before the setup.
function serverMarket(all: ReturnType<typeof pair>[]) {
  return (request: Request) => {
    const query = new URL(request.url).searchParams;
    const interval = query.get('Interval') ?? '15';
    const has = (row: (typeof all)[number], setup: string) => row.setups[interval]!.includes(setup);
    const left = all.filter(
      (row) =>
        (!query.has('Category') || row.category === query.get('Category')) &&
        (!query.has('Search') || row.symbol.includes(query.get('Search')!.toUpperCase())),
    );
    const items = left.filter((row) => !query.has('Setup') || has(row, query.get('Setup')!));
    const counts = Object.fromEntries(
      ['impulse', 'early_impulse', 'drop', 'early_drop', 'high_volatility'].map((setup) => [
        setup,
        left.filter((row) => has(row, setup)).length,
      ]),
    );

    return json(
      marketSnapshot(items, {
        pairs: all.length,
        total: items.length,
        setups: { all: left.length, ...counts },
      }),
    );
  };
}

const lastRead = (requests: Request[]) =>
  Object.fromEntries(
    new URL(
      requests.filter((request) => new URL(request.url).pathname === '/api/market').at(-1)!.url,
    ).searchParams,
  );

const chips = () => within(screen.getByRole('group', { name: 'Сетап' }));

describe('market setups', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Импульсы на 15 минутах".
  it('asks for the pairs with an impulse on 15 minutes when the chip is chosen', async () => {
    const requests = fakeApi({ 'GET /api/market': serverMarket([wif, sol, doge, eth]) });
    renderApp('/market');

    await screen.findByRole('table');
    expect(chips().getByRole('button', { name: 'Все 4' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));

    await waitFor(() =>
      expect(lastRead(requests)).toMatchObject({ Setup: 'impulse', Interval: '15' }),
    );
    expect(await screen.findByText('2 из 4 инструментов')).toBeInTheDocument();
    expect(symbolsIn(screen.getByRole('table')).sort()).toEqual(['SOLUSDT', 'WIFUSDT']);
  });

  it('shows the chips with the number of pairs of each setup and the setups of a pair as badges', async () => {
    fakeApi({ 'GET /api/market': () => json(marketSnapshot([wif, sol, doge, eth])) });
    renderApp('/market');

    const row = (await screen.findByRole('link', { name: 'WIFUSDT' })).closest('tr')!;

    expect(
      chips()
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual([
      'Все 4',
      'Импульс 2',
      'Ранний импульс 0',
      'Падение 1',
      'Раннее падение 0',
      'Высокая волатильность 1',
    ]);
    // Scenario "Два сетапа у пары".
    expect(within(row).getByText('Импульс')).toBeInTheDocument();
    expect(within(row).getByText('Высокая волатильность')).toBeInTheDocument();
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', { name: 'Сетап · 15 мин' }),
    ).toBeInTheDocument();
  });

  it('reads the setups on the interval chosen with the switch, 15 minutes at first', async () => {
    const requests = fakeApi({ 'GET /api/market': serverMarket([wif, sol, doge, eth]) });
    renderApp('/market');
    await screen.findByRole('table');
    const switchOf = within(screen.getByRole('group', { name: 'ТФ сетапа' }));

    expect(switchOf.getAllByRole('button').map((button) => button.textContent)).toEqual([
      '5 мин',
      '15 мин',
      '1 ч',
      '4 ч',
    ]);
    expect(switchOf.getByRole('button', { name: '15 мин' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(lastRead(requests)).toMatchObject({ Interval: '15' });
    await userEvent.click(switchOf.getByRole('button', { name: '5 мин' }));

    expect(await chips().findByRole('button', { name: 'Падение 1' })).toBeInTheDocument();
    expect(chips().getByRole('button', { name: 'Импульс 0' })).toBeInTheDocument();
    expect(lastRead(requests)).toMatchObject({ Interval: '5' });
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', { name: 'Сетап · 5 мин' }),
    ).toBeInTheDocument();
  });

  // Scenario "Чипы считают остальные фильтры".
  it('combines the chip with the search, and the chips count what the search leaves', async () => {
    const requests = fakeApi({ 'GET /api/market': serverMarket([wif, sol, doge, eth]) });
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'sol');

    await waitFor(() =>
      expect(lastRead(requests)).toMatchObject({ Setup: 'impulse', Search: 'sol' }),
    );
    expect(await screen.findByText('1 из 4 инструментов')).toBeInTheDocument();
    expect(symbolsIn(screen.getByRole('table'))).toEqual(['SOLUSDT']);
    expect(chips().getByRole('button', { name: 'Все 1' })).toBeInTheDocument();
  });

  it('keeps the chip and the interval when the table is read again', async () => {
    let answer = serverMarket([wif, sol, doge, eth]);
    const requests = fakeApi({ 'GET /api/market': (request) => answer(request) });
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));
    await waitFor(() => expect(lastRead(requests)).toMatchObject({ Setup: 'impulse' }));
    answer = serverMarket([wif, { ...sol, change1h: '9' }, pair('NEWUSDT', { '15': ['impulse'] })]);

    await act(() => vi.advanceTimersByTimeAsync(15_000));

    await waitFor(() =>
      expect(symbolsIn(screen.getByRole('table')).sort()).toEqual([
        'NEWUSDT',
        'SOLUSDT',
        'WIFUSDT',
      ]),
    );
    expect(chips().getByRole('button', { name: 'Импульс 3' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(lastRead(requests)).toMatchObject({ Setup: 'impulse', Interval: '15' });
    expect(
      within(screen.getByRole('group', { name: 'ТФ сетапа' })).getByRole('button', {
        name: '15 мин',
      }),
    ).toHaveAttribute('aria-pressed', 'true');
  });

  it('says that nothing is found when no pair has the setup', async () => {
    fakeApi({ 'GET /api/market': serverMarket([wif, sol]) });
    renderApp('/market');
    await screen.findByRole('table');

    await userEvent.click(chips().getByRole('button', { name: 'Падение 0' }));

    expect(await screen.findByText('Ничего не найдено.')).toBeInTheDocument();
  });
});
