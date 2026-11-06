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

const snapshotReads = (requests: Request[]) =>
  requests.filter((request) => new URL(request.url).pathname === '/api/market');
const lastRead = (requests: Request[]) =>
  Object.fromEntries(new URL(snapshotReads(requests).at(-1)!.url).searchParams);
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
    fakeApi({
      'GET /api/market': (request) =>
        json(
          new URL(request.url).searchParams.get('Category') === 'spot'
            ? marketSnapshot([eth, doge], { pairs: 4, categories: { linear: 2, spot: 2 } })
            : marketSnapshot([sol, wif, eth, doge]),
        ),
    });
    renderApp('/market');
    await tableOf();

    expect(screen.getByText('4 из 4 инструментов')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'spot 2' }));
    expect(await screen.findByText('2 из 4 инструментов')).toBeInTheDocument();
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
      'Сетап · 15 мин',
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

  // Scenario "Поиск на второй странице рынка": the API filters; the screen names the category and the search.
  it('asks for a category with the number of pairs and for a search by part of the symbol', async () => {
    const requests = open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    expect(
      within(screen.getByRole('group', { name: 'Категория' })).getByRole('button', {
        name: 'Все 4',
      }),
    ).toHaveAttribute('aria-pressed', 'true');
    expect(lastRead(requests)).toEqual({
      Interval: '15',
      Sort: 'change1h',
      Direction: 'desc',
      Page: '1',
      PageSize: '50',
    });
    await userEvent.click(screen.getByRole('button', { name: 'spot 2' }));
    await waitFor(() => expect(lastRead(requests)).toMatchObject({ Category: 'spot' }));

    await userEvent.click(
      within(screen.getByRole('group', { name: 'Категория' })).getByRole('button', {
        name: 'Все 4',
      }),
    );
    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'ge');
    await waitFor(() => expect(lastRead(requests)).toMatchObject({ Search: 'ge' }));
    expect(lastRead(requests)).not.toHaveProperty('Category');
  });

  // The search goes after a pause of 300 ms in the typing, not on every letter.
  it('sends the search once, after the pause in the typing', async () => {
    const requests = open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();
    const before = snapshotReads(requests).length;

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'sol');

    expect(snapshotReads(requests)).toHaveLength(before);
    await act(() => vi.advanceTimersByTimeAsync(300));
    await waitFor(() => expect(lastRead(requests)).toMatchObject({ Search: 'sol' }));
    expect(snapshotReads(requests)).toHaveLength(before + 1);
  });

  it('never asks for the ideas', async () => {
    const requests = open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();

    expect(requests.some((request) => new URL(request.url).pathname === '/api/ideas')).toBe(false);
  });

  // Scenario "Сортировка по изменению за 1 час": as in the mockup, the table opens sorted by it, growth first; the API sorts.
  it('asks to sort by the change over an hour from the start and the other way on a click', async () => {
    const requests = open(() => marketSnapshot([doge, sol, wif, eth]));
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

    await waitFor(() =>
      expect(lastRead(requests)).toMatchObject({ Sort: 'change1h', Direction: 'asc' }),
    );
    expect(hour).toHaveAttribute('aria-sort', 'ascending');
    expect(hour).toHaveTextContent('1ч ↑');
  });

  it('asks to sort by symbol from A to Z first', async () => {
    const requests = open(() => marketSnapshot([sol, wif, eth, doge]));
    renderApp('/market');
    await tableOf();

    await userEvent.click(screen.getByRole('button', { name: 'Инструмент' }));

    await waitFor(() =>
      expect(lastRead(requests)).toMatchObject({ Sort: 'symbol', Direction: 'asc' }),
    );
  });

  // Scenario "Новая минута".
  it('reads the snapshot every 15 seconds with the same filter, search and sort', async () => {
    let answer = marketSnapshot([sol, wif]);
    const requests = open(() => answer);
    renderApp('/market');
    await tableOf();
    await userEvent.click(screen.getByRole('button', { name: /^linear/ }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'USDT');
    await waitFor(() => expect(lastRead(requests)).toMatchObject({ Search: 'USDT' }));
    await waitFor(() =>
      expect(symbolsIn(screen.getByRole('table'))).toEqual(['SOLUSDT', 'WIFUSDT']),
    );
    const asked = snapshotReads(requests).length;
    answer = marketSnapshot([
      { ...wif, change1h: '-1' },
      { ...sol, change1h: '-5' },
    ]);

    await act(() => vi.advanceTimersByTimeAsync(15_000));

    await waitFor(() => expect(snapshotReads(requests)).toHaveLength(asked + 1));
    expect(lastRead(requests)).toMatchObject({ Category: 'linear', Search: 'USDT' });
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
    fakeApi({
      'GET /api/market': (request) =>
        json(
          new URL(request.url).searchParams.has('Search')
            ? marketSnapshot([], { pairs: 1 })
            : marketSnapshot([sol]),
        ),
    });
    renderApp('/market');
    await tableOf();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'zzz');

    expect(await screen.findByText('Ничего не найдено.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'На странице' })).not.toBeInTheDocument();
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

  it('gives the table the whole width of the window', async () => {
    open(() => marketSnapshot([sol]));
    renderApp('/market');
    await tableOf();

    expect(screen.getByRole('main')).toHaveClass('max-w-none');
  });

  describe('the pages', () => {
    // 1 104 pairs, the page the request names; the pairs of a page are named after their number.
    const pagedApi = (total = 1104) =>
      fakeApi({
        'GET /api/market': (request) => {
          const query = new URL(request.url).searchParams;
          const size = Number(query.get('PageSize') ?? 50);
          const last = Math.ceil(total / size);
          const page = Math.min(Number(query.get('Page') ?? 1), last);
          const from = (page - 1) * size;
          const rows = Array.from({ length: Math.min(size, total - from) }, (_, index) =>
            marketRow({ symbol: `P${from + index + 1}USDT` }),
          );

          return json(marketSnapshot(rows, { page, pageSize: size, total, pairs: total }));
        },
        'GET /api/saved-filters': () => json([]),
        'GET /api/saved-filters/fields': () => json([]),
      });

    // Scenario "Вторая страница рынка".
    it('turns the pages with the numbers of the rows shown and writes the page into the address', async () => {
      localStorage.setItem('profit-fox.market.choice', JSON.stringify({ pageSize: 100 }));
      const requests = pagedApi();
      const { history } = renderApp('/market');
      await screen.findByText('1–100 из 1 104');
      expect(screen.getByText('1 из 12')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Следующая страница' }));

      expect(await screen.findByText('101–200 из 1 104')).toBeInTheDocument();
      expect(lastRead(requests)).toMatchObject({ Page: '2', PageSize: '100' });
      expect(history.location.search).toBe('?page=2');
      localStorage.clear();
    });

    it('goes back to the first page when the search, the category or the sorting changes', async () => {
      const requests = pagedApi();
      const { history } = renderApp('/market?page=3');
      await screen.findByText('101–150 из 1 104');

      await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'btc');

      await waitFor(() => expect(lastRead(requests)).toMatchObject({ Search: 'btc', Page: '1' }));
      expect(history.location.search).toBe('');
    });

    it('does not ask for the first page of the old search while the search is typed', async () => {
      const requests = pagedApi();
      renderApp('/market?page=3');
      await screen.findByText('101–150 из 1 104');

      await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по символу' }), 'btc');

      await waitFor(() => expect(lastRead(requests)).toMatchObject({ Search: 'btc', Page: '1' }));
      expect(
        snapshotReads(requests).filter(
          (request) =>
            new URL(request.url).searchParams.get('Page') === '1' &&
            !new URL(request.url).searchParams.has('Search'),
        ),
      ).toHaveLength(0);
    });

    it.each([
      [
        'the category',
        () => userEvent.click(screen.getByRole('button', { name: /^spot/ })),
        { Category: 'spot' },
      ],
      [
        'the sorting',
        () => userEvent.click(screen.getByRole('button', { name: 'Инструмент' })),
        { Sort: 'symbol' },
      ],
      [
        'the size of a page',
        () => userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '200'),
        { PageSize: '200' },
      ],
    ])('goes back to the first page on a change of %s', async (_name, change, expected) => {
      const requests = pagedApi();
      const { history } = renderApp('/market?page=3');
      await screen.findByText('101–150 из 1 104');

      await change();

      await waitFor(() => expect(lastRead(requests)).toMatchObject({ ...expected, Page: '1' }));
      expect(history.location.search).toBe('');
    });

    // Scenario "Размер страницы".
    it('remembers the size of a page in the browser', async () => {
      pagedApi();
      const first = renderApp('/market');
      await screen.findByText('1–50 из 1 104');
      await userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '200');
      await screen.findByText('1–200 из 1 104');
      first.unmount();

      renderApp('/market');

      expect(await screen.findByText('1–200 из 1 104')).toBeInTheDocument();
      localStorage.clear();
    });

    it('moves the address to the last page when the table became shorter', async () => {
      pagedApi(80);
      const { history } = renderApp('/market?page=3');

      expect(await screen.findByText('51–80 из 80')).toBeInTheDocument();
      await waitFor(() => expect(history.location.search).toBe('?page=2'));
    });
  });
});
