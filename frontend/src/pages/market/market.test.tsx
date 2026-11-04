import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { marketRow, marketSnapshot } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const now = Date.parse('2026-11-04T12:00:00Z');
const ago = (ms: number) => new Date(now - ms).toISOString();

const sol = marketRow({
  symbol: 'SOLUSDT',
  price: '168.42',
  change5m: '0.1',
  change15m: '-0.25',
  change1h: '3.2',
  change4h: '0',
  change24h: '12.4',
  turnover1h: '12400000',
  turnover24h: '3500000000',
  openInterest: '25300',
  openInterestChange1h: '1.5',
  openInterestChange24h: '-2',
  fundingRate: '0.0001',
  spread: '0.012',
});
const wif = marketRow({ symbol: 'WIFUSDT', change1h: '-1.5', incomplete: true });
const eth = marketRow({ category: 'spot', symbol: 'ETHUSDT', price: '2400', change1h: null });
const doge = marketRow({ category: 'spot', symbol: 'DOGEUSDT', change1h: '10' });

function open(answer: () => unknown) {
  return fakeApi({ 'GET /api/market': () => json(answer()) });
}

const tableOf = () => screen.findByRole('table');
const symbolsIn = (table: HTMLElement) =>
  within(table)
    .getAllByRole('link')
    .map((link) => link.textContent);

describe('market screen', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(now);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Снимок есть".
  it('shows a pair with its metrics, changes coloured and funding signed without colour', async () => {
    open(() => marketSnapshot([sol], { snapshotAt: ago(14_000) }));
    renderApp('/market');

    const row = (await screen.findByRole('link', { name: 'SOLUSDT' })).closest('tr')!;
    // The category is a badge in front of the symbol; the unit of a column is in its header, not in the cells.
    expect(within(row).getByText('linear')).toHaveClass('text-run');
    expect(
      within(row).getByText('linear').compareDocumentPosition(within(row).getByText('SOLUSDT')),
    ).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(within(row).getByText('168.42')).toBeInTheDocument();
    expect(within(row).getByText('+0.10')).toHaveClass('text-ok');
    expect(within(row).getByText('-0.25')).toHaveClass('text-err');
    expect(within(row).getByText('+3.20')).toHaveClass('text-ok');
    expect(within(row).getByText('0.00')).not.toHaveClass('text-ok', 'text-err');
    expect(within(row).getByText('+12.40')).toHaveClass('text-ok');
    expect(within(row).getByText('12.4 млн')).toBeInTheDocument();
    expect(within(row).getByText('3.50 млрд')).toBeInTheDocument();
    expect(within(row).getByText('25.3 тыс.')).toBeInTheDocument();
    expect(within(row).getByText('+1.5')).toHaveClass('text-ok');
    expect(within(row).getByText('-2.0')).toHaveClass('text-err');
    expect(within(row).getByText('+0.0100')).not.toHaveClass('text-ok');
    expect(within(row).getByText('0.01')).toBeInTheDocument();
    expect(screen.getByText('Обновлено 14 с назад')).not.toHaveClass('text-err');
  });

  it('counts the pairs it shows and says what the columns are', async () => {
    open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    expect(screen.getByText('4 инструмента')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'spot 2' }));
    expect(screen.getByText('2 инструмента')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Изменения и оборот — по закрытым минутным свечам. OI, фандинг и спред обновляются раз в минуту, у spot их нет («—»). Фандинг — текущая ставка за период.',
      ),
    ).toBeInTheDocument();
  });

  // Scenario "Пара spot в таблице".
  it('shows a dash where a pair has no metric, and for open interest and funding of a spot pair', async () => {
    open(() => marketSnapshot([eth]));
    renderApp('/market');

    const row = (await screen.findByRole('link', { name: 'ETHUSDT' })).closest('tr')!;
    // 5 min … 24 h, both turnovers, open interest and its two changes, funding and spread: 12 empty cells.
    expect(within(row).getAllByText('—')).toHaveLength(12);
  });

  it('names the columns', async () => {
    open(() => marketSnapshot([sol]));
    renderApp('/market');

    await tableOf();
    const names = within(screen.getByRole('table'))
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
    // Groups of columns above, the columns of a group under them.
    expect(names).toEqual([
      'Инструмент',
      'Цена',
      'Изменение, %',
      'Оборот, USDT',
      'OI, USDT',
      'ΔOI, %',
      'Фандинг, %',
      'Спред, %',
      '5м',
      '15м',
      '1ч ↓',
      '4ч',
      '24ч',
      '1ч',
      '24ч',
      '1ч',
      '24ч',
    ]);
  });

  it('filters by category with the number of pairs and searches by part of the symbol', async () => {
    open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    expect(screen.getByRole('button', { name: 'Все 4' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'spot 2' }));
    expect(symbolsIn(screen.getByRole('table'))).toEqual(['DOGEUSDT', 'ETHUSDT']);

    await userEvent.click(screen.getByRole('button', { name: 'Все 4' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'ge');
    expect(symbolsIn(screen.getByRole('table'))).toEqual(['DOGEUSDT']);
  });

  // Scenario "Сортировка по изменению за 1 час": as in the mockup, the table opens sorted by it, growth first.
  it('sorts by the change over an hour from the start and the other way on a click, the empty values last', async () => {
    open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    expect(symbolsIn(screen.getByRole('table'))).toEqual([
      'DOGEUSDT',
      'SOLUSDT',
      'WIFUSDT',
      'ETHUSDT',
    ]);
    const hour = screen.getByRole('button', { name: 'Изменение за 1ч' }).closest('th')!;
    expect(hour).toHaveAttribute('aria-sort', 'descending');
    expect(hour).toHaveTextContent('1ч ↓');

    await userEvent.click(screen.getByRole('button', { name: 'Изменение за 1ч' }));
    expect(symbolsIn(screen.getByRole('table'))).toEqual([
      'WIFUSDT',
      'SOLUSDT',
      'DOGEUSDT',
      'ETHUSDT',
    ]);
    expect(hour).toHaveAttribute('aria-sort', 'ascending');
    expect(hour).toHaveTextContent('1ч ↑');
  });

  it('sorts by symbol from A to Z first', async () => {
    open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    await userEvent.click(screen.getByRole('button', { name: 'Инструмент' }));

    expect(symbolsIn(screen.getByRole('table'))).toEqual([
      'DOGEUSDT',
      'ETHUSDT',
      'SOLUSDT',
      'WIFUSDT',
    ]);
  });

  // Scenario "Новая минута".
  it('reads the snapshot every 15 seconds and keeps the filter, the search and the sort', async () => {
    let answer = marketSnapshot([sol, wif, doge, eth]);
    const requests = open(() => answer);
    renderApp('/market');
    await tableOf();
    await userEvent.click(screen.getByRole('button', { name: 'linear 2' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'USDT');
    expect(symbolsIn(screen.getByRole('table'))).toEqual(['SOLUSDT', 'WIFUSDT']);
    answer = marketSnapshot([{ ...sol, change1h: '-5' }, { ...wif, change1h: '-1' }, doge, eth]);

    await act(() => vi.advanceTimersByTimeAsync(15_000));

    await waitFor(() => expect(requests).toHaveLength(2));
    await waitFor(() =>
      expect(symbolsIn(screen.getByRole('table'))).toEqual(['WIFUSDT', 'SOLUSDT']),
    );
    expect(screen.getByText('-1.00')).toBeInTheDocument();
  });

  it('lets the age of the snapshot grow between two reads', async () => {
    open(() => marketSnapshot([sol], { snapshotAt: ago(14_000) }));
    renderApp('/market');
    await screen.findByText('Обновлено 14 с назад');

    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(await screen.findByText('Обновлено 19 с назад')).toBeInTheDocument();
  });

  // Scenario "Первая загрузка".
  it('shows how much of the history is loaded while it is not whole', async () => {
    open(() => marketSnapshot([sol], { history: { loaded: 312, total: 1104 } }));
    renderApp('/market');

    const banner = await screen.findByRole('status', { name: 'Загружается история за 90 дней' });
    expect(within(banner).getByText('История: 312 из 1 104 инструментов')).toBeInTheDocument();
    expect(within(banner).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '312');
    expect(within(banner).getByRole('progressbar')).toHaveAttribute('aria-valuemax', '1104');
  });

  it('shows no history banner once the history is whole', async () => {
    open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();

    expect(screen.queryByText(/История:/)).not.toBeInTheDocument();
  });

  // Scenario "Сканер на паузе".
  it('says that Bybit limited the requests with the end of the pause, and goes on showing the table', async () => {
    open(() =>
      marketSnapshot([sol], {
        restPausedUntil: '2026-11-04T14:32:00Z',
        history: { loaded: 640, total: 1104 },
      }),
    );
    renderApp('/market');

    const banner = await screen.findByRole('status', {
      name: 'Bybit ограничил запросы — догрузка истории на паузе до 14:32 UTC',
    });
    expect(
      within(banner).getByText('История: 640 из 1 104 инструментов, пауза'),
    ).toBeInTheDocument();
    // One panel tells of the history while it pauses.
    expect(
      screen.queryByRole('status', { name: 'Загружается история за 90 дней' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  // Scenario "Сканер не отвечает".
  it('warns about stale data and dims the table', async () => {
    open(() => marketSnapshot([sol], { snapshotAt: ago(7 * 60_000) }));
    renderApp('/market');

    const banner = await screen.findByRole('status', {
      name: 'Данные устарели: последнее обновление 7 мин назад, сканер не отвечает',
    });
    expect(
      within(banner).getByText(/Значения ниже — на 11:53 UTC и не меняются/),
    ).toBeInTheDocument();
    expect(screen.getByRole('table')).toHaveClass('opacity-50');
    expect(screen.getByText('Обновлено 7 мин назад')).toHaveClass('text-err');
  });

  it('does not dim the table of a fresh snapshot', async () => {
    open(() => marketSnapshot([sol], { snapshotAt: ago(60_000) }));
    renderApp('/market');

    expect(await tableOf()).not.toHaveClass('opacity-50');
    expect(screen.queryByText(/Данные устарели/)).not.toBeInTheDocument();
  });

  it('says that the scanner has not collected data yet when there is no snapshot', async () => {
    open(() => marketSnapshot([], { snapshotAt: null }));
    renderApp('/market');

    expect(await screen.findByText('Сканер ещё не собрал данные.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  // Scenario "Пропуск догружается".
  it('marks a pair whose data is incomplete', async () => {
    open(() => marketSnapshot([sol, wif]));
    renderApp('/market');

    const row = (await screen.findByRole('link', { name: 'WIFUSDT' })).closest('tr')!;
    expect(within(row).getByRole('img', { name: 'Данные догружаются' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('link', { name: 'SOLUSDT' }).closest('tr')!).queryByRole('img'),
    ).not.toBeInTheDocument();
  });

  it('opens the page of the pair when its row is clicked', async () => {
    open(() => marketSnapshot([sol, eth]));
    const { history } = renderApp('/market');

    await userEvent.click(await screen.findByText('2 400'));

    await waitFor(() => expect(history.location.pathname).toBe('/market/spot/ETHUSDT'));
  });

  it('opens the pair once when its symbol link is clicked', async () => {
    open(() => marketSnapshot([sol]));
    const { history } = renderApp('/market');
    const push = vi.spyOn(history, 'push');

    await userEvent.click(await screen.findByRole('link', { name: 'SOLUSDT' }));

    await waitFor(() => expect(history.location.pathname).toBe('/market/linear/SOLUSDT'));
    expect(push).toHaveBeenCalledTimes(1);
  });

  it('says that no pair matches the search', async () => {
    open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'zzz');

    expect(screen.getByText('Ничего не найдено.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('says that the market is loading', async () => {
    fakeApi({ 'GET /api/market': () => new Promise<Response>(() => undefined) });
    renderApp('/market');

    expect(await screen.findByText('Загрузка рынка…')).toBeInTheDocument();
  });

  it('says that the market could not be loaded', async () => {
    fakeApi({ 'GET /api/market': () => problem(500, 'boom') });
    renderApp('/market');

    // A server error is read once more, a second later.
    expect(await screen.findByRole('alert', {}, { timeout: 3000 })).toHaveTextContent(
      'Не удалось загрузить рынок.',
    );
  });

  it('gives the table more width than other screens', async () => {
    open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();

    expect(screen.getByRole('main')).toHaveClass('max-w-[1376px]');
  });
});
