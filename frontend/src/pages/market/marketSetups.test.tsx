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
  it('keeps the pairs with an impulse on 15 minutes when the chip is chosen', async () => {
    fakeApi({ 'GET /api/market': () => json(marketSnapshot([wif, sol, doge, eth])) });
    renderApp('/market');

    await screen.findByRole('table');
    expect(chips().getByRole('button', { name: 'Все 4' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));

    expect(symbolsIn(screen.getByRole('table')).sort()).toEqual(['SOLUSDT', 'WIFUSDT']);
    expect(screen.getByText('2 из 4 инструментов')).toBeInTheDocument();
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
    fakeApi({ 'GET /api/market': () => json(marketSnapshot([wif, sol, doge, eth])) });
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
    await userEvent.click(switchOf.getByRole('button', { name: '5 мин' }));

    expect(chips().getByRole('button', { name: 'Падение 1' })).toBeInTheDocument();
    expect(chips().getByRole('button', { name: 'Импульс 0' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', { name: 'Сетап · 5 мин' }),
    ).toBeInTheDocument();
  });

  it('combines the chip with the category and the search', async () => {
    fakeApi({ 'GET /api/market': () => json(marketSnapshot([wif, sol, doge, eth])) });
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'sol');

    expect(symbolsIn(screen.getByRole('table'))).toEqual(['SOLUSDT']);
    expect(screen.getByText('1 из 4 инструментов')).toBeInTheDocument();
  });

  it('keeps the chip and the interval when the table is read again', async () => {
    let answer = marketSnapshot([wif, sol, doge, eth]);
    fakeApi({ 'GET /api/market': () => json(answer) });
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(chips().getByRole('button', { name: 'Импульс 2' }));
    answer = marketSnapshot([
      wif,
      { ...sol, change1h: '9' },
      pair('NEWUSDT', { '15': ['impulse'] }),
    ]);

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
    expect(
      within(screen.getByRole('group', { name: 'ТФ сетапа' })).getByRole('button', {
        name: '15 мин',
      }),
    ).toHaveAttribute('aria-pressed', 'true');
  });

  it('says that nothing is found when no pair has the setup', async () => {
    fakeApi({ 'GET /api/market': () => json(marketSnapshot([wif, sol])) });
    renderApp('/market');
    await screen.findByRole('table');

    await userEvent.click(chips().getByRole('button', { name: 'Падение 0' }));

    expect(screen.getByText('Ничего не найдено.')).toBeInTheDocument();
  });
});
