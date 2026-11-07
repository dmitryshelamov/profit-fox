import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  autoItemOf,
  autoOf,
  exchangeAccount,
  guardsOf,
  journalOf,
  journalTotals,
  refusalOf,
  refusalsPage,
  tradeOf,
  tradingSettings,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Trade = ReturnType<typeof tradeOf>;

/** The journal the test names: the trades in work and the closed ones; the rest of the API is quiet. */
function routes(
  journal: () => object,
  extra: Record<string, (request: Request) => Response | Promise<Response>> = {},
) {
  return {
    'GET /api/idea-trades': () => json(journal()),
    'GET /api/idea-trading/settings': () => json(tradingSettings),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    'GET /api/idea-autos': () => json([]),
    'GET /api/idea-trading/guards': () => json(guardsOf()),
    'GET /api/idea-autos/refusals': () => json(refusalsPage()),
    ...extra,
  };
}

const open = (trades: Trade[]) => () => journalOf(trades);

const lastQuery = (requests: Request[]) =>
  Object.fromEntries(
    new URL(requests.findLast((request) => request.url.includes('/api/idea-trades?'))!.url)
      .searchParams,
  );

afterEach(() => {
  localStorage.clear();
  vi.useRealTimers();
});

describe('screen «Сделки»', () => {
  // Requirement "Группа «В работе»", scenario "Открытая сделка".
  it('shows an open trade with the time in it, what it is worth now and the button to close it', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-05T12:15:00Z'));
    fakeApi(
      routes(
        open([
          tradeOf({
            status: 'open',
            openedAt: '2026-11-05T10:00:00Z',
            lastPrice: '168.9',
            currentResult: '0.21',
            currentResultR: '0.21',
            promise: {
              badge: 'up',
              probability: '0.31',
              expectancy: '0.14',
              cases: 412,
              breakEven: '0.351',
              netRiskReward: '1.8435',
            },
          }),
        ]),
      ),
    );
    renderApp('/idea-trades');

    const table = await screen.findByRole('table', { name: 'Сделки в работе' });
    const row = within(table).getAllByRole('row')[1]!;

    expect(within(row).getByText('SOLUSDT')).toBeInTheDocument();
    expect(within(row).getByText('Открыта')).toBeInTheDocument();
    expect(within(row).getByText('05.11 13:00')).toBeInTheDocument();
    expect(within(row).getByText('2 ч 15 мин')).toBeInTheDocument();
    expect(within(row).getByText('+0.21 USDT · +0.21 R')).toBeInTheDocument();
    expect(within(row).getByText('▲ +0.14 R · N 412')).toHaveAttribute(
      'title',
      'Вероятность 31 % · безубыток: нужно ≥ 36 % · R:R 1.84',
    );
    for (const value of ['168.4', '165', '175', '0.47', '0.99']) {
      expect(within(row).getByText(value)).toBeInTheDocument();
    }
    expect(within(row).getByRole('button', { name: 'Закрыть по рынку' })).toBeInTheDocument();
  });

  // The average of the fills and the risk come with every decimal the exchange gave; they would stretch the table.
  it('shows the fill price by the decimals of the entry and the risk in cents', async () => {
    fakeApi(
      routes(
        open([
          tradeOf({
            status: 'open',
            openedAt: '2026-11-05T10:00:00Z',
            entryPrice: '0.02064',
            filledPrice: '0.0206137501892964230547899567',
            stop: '0.02093',
            target: '0.01994',
            risk: '1.9789585200000000000000000003',
          }),
        ]),
      ),
    );
    renderApp('/idea-trades');

    const table = await screen.findByRole('table', { name: 'Сделки в работе' });
    const row = within(table).getAllByRole('row')[1]!;

    expect(within(row).getByText('0.02061')).toBeInTheDocument();
    expect(within(row).getByText('1.98')).toBeInTheDocument();
  });

  // The time in a trade runs on while the data stands still.
  it('lets the time in a trade run on without a new answer of the API', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-11-05T12:15:00Z'));
    fakeApi(routes(open([tradeOf({ status: 'open', openedAt: '2026-11-05T10:00:00Z' })])));
    renderApp('/idea-trades');
    expect(await screen.findByText('2 ч 15 мин')).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    expect(screen.getByText('2 ч 16 мин')).toBeInTheDocument();
  });

  // Scenario "Цены нет".
  it('shows a dash instead of the result of a trade that has no price, and for one that waits', async () => {
    fakeApi(
      routes(
        open([
          tradeOf({ id: 'a', status: 'open', openedAt: '2026-11-05T10:00:00Z' }),
          tradeOf({ id: 'b' }),
        ]),
      ),
    );
    renderApp('/idea-trades');

    const rows = within(await screen.findByRole('table', { name: 'Сделки в работе' })).getAllByRole(
      'row',
    );
    const now = (row: HTMLElement) => within(row).getAllByRole('cell')[12]!.textContent;

    expect(now(rows[1]!)).toBe('—');
    expect(now(rows[2]!)).toBe('—');
    expect(within(rows[2]!).getByText('Ждёт входа')).toBeInTheDocument();
    expect(within(rows[2]!).getByRole('button', { name: 'Отменить' })).toBeInTheDocument();
  });

  it('says that nothing is in work when no trade is open', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades');

    expect(await screen.findByText('В работе ничего нет.')).toBeInTheDocument();
    expect(screen.getByText('Закрытых сделок по этим условиям нет.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Сделки' })).toBeInTheDocument();
  });

  // Requirement "Таблица закрытых сделок", scenario "Сделка с обещанием и исходом идеи".
  it('shows a closed trade with its result, the time to the outcome, the promise and the outcome of its idea', async () => {
    fakeApi(
      routes(() =>
        journalOf(
          [],
          [
            tradeOf({
              status: 'closed_at_stop',
              statusAt: '2026-11-05T13:30:00Z',
              result: '-1.05',
              resultR: '-1.06',
              secondsToOutcome: 12_600,
              promise: {
                badge: 'up',
                probability: '0.31',
                expectancy: '0.14',
                cases: 412,
                breakEven: '0.351',
                netRiskReward: '1.8435',
              },
              ideaOutcome: { status: 'target', statusAt: '2026-11-05T14:00:00Z', result: '1.84' },
            }),
          ],
        ),
      ),
    );
    renderApp('/idea-trades');

    const table = await screen.findByRole('table', { name: 'Закрытые сделки' });
    const row = within(table).getAllByRole('row')[1]!;

    expect(within(row).getByText('05.11 16:30')).toBeInTheDocument();
    expect(within(row).getByText('Закрыта по стопу')).toBeInTheDocument();
    expect(within(row).getByText('-1.05 USDT · -1.06 R')).toBeInTheDocument();
    expect(within(row).getByText('3 ч 30 мин')).toBeInTheDocument();
    expect(within(row).getByText('▲ +0.14 R · N 412')).toBeInTheDocument();
    expect(within(row).getByText('Цель +1.84 R')).toBeInTheDocument();
    expect(within(row).queryByRole('button')).toBeNull();
  });

  // Scenario "Сделка до выкатки".
  it('shows dashes in the columns of the promise and of the idea of a trade made before the journal', async () => {
    fakeApi(
      routes(() =>
        journalOf([], [tradeOf({ status: 'closed_manually', result: '0.31', resultR: '0.3' })]),
      ),
    );
    renderApp('/idea-trades');

    const row = within(await screen.findByRole('table', { name: 'Закрытые сделки' })).getAllByRole(
      'row',
    )[1]!;
    const cells = within(row).getAllByRole('cell');

    expect(cells.slice(-2).map((cell) => cell.textContent)).toEqual(['—', '—']);
  });

  it('shows the reason of a trade in «Ошибка» and of one that is not failed', async () => {
    fakeApi(
      routes(() =>
        journalOf(
          [
            tradeOf({
              id: 'a',
              status: 'open',
              reason: 'Закрытие по рынку отклонено: Reduce-only rejected.',
            }),
          ],
          [
            tradeOf({
              id: 'b',
              status: 'failed',
              reason: 'Exchange position 0 does not match the positions.',
            }),
            tradeOf({ id: 'c', status: 'cancelled', reason: 'Вход не ушёл вовремя.' }),
          ],
        ),
      ),
    );
    renderApp('/idea-trades');

    expect(
      await screen.findByText('Закрытие по рынку отклонено: Reduce-only rejected.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Exchange position 0 does not match the positions.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Вход не ушёл вовремя.')).toBeInTheDocument();
  });

  // Requirement "Итоги на экране «Сделки»".
  it('shows the totals of the closed trades', async () => {
    fakeApi(
      routes(() =>
        journalOf([], [tradeOf({ status: 'closed_at_target', result: '1', resultR: '1' })], {
          totals: journalTotals({
            targets: 1,
            stops: 1,
            manual: 1,
            cancelled: 2,
            result: '0.9',
            fees: '0.12',
            meanR: '0.3',
            meanRLow: '-1.3437',
            meanRHigh: '1.9437',
            meanSecondsToOutcome: 5400,
            promise: { count: 3, promised: '0.15', got: '-0.1333' },
            ideas: { count: 2, ideas: '0.45', trades: '0.35' },
          }),
        }),
      ),
    );
    renderApp('/idea-trades');

    const totals = await screen.findByRole('region', { name: 'Итоги' });

    expect(
      within(totals).getByText('по цели 1 (33 %) · по стопу 1 (33 %) · вручную 1 (33 %)'),
    ).toBeInTheDocument();
    expect(within(totals).getByText('+0.90 USDT · комиссии 0.12 USDT')).toBeInTheDocument();
    expect(within(totals).getByText('+0.30 R (95 %: -1.34 … +1.94)')).toBeInTheDocument();
    expect(within(totals).getByText('1 ч 30 мин')).toBeInTheDocument();
    expect(
      within(totals).getByText('обещали +0.15 R → получили -0.13 R · N 3'),
    ).toBeInTheDocument();
    expect(within(totals).getByText('идеи +0.45 R → сделки +0.35 R · N 2')).toBeInTheDocument();
    expect(within(totals).getByText('отменено 2: не входят в результат и R.')).toBeInTheDocument();
  });

  // Scenario "Сравнивать не с чем".
  it('shows dashes for the comparisons nothing stands behind', async () => {
    fakeApi(
      routes(() =>
        journalOf([], [tradeOf({ status: 'closed_at_target', result: '1', resultR: '1' })], {
          totals: journalTotals({ targets: 1, meanR: '1' }),
        }),
      ),
    );
    renderApp('/idea-trades');

    const totals = await screen.findByRole('region', { name: 'Итоги' });

    expect(within(totals).getAllByText('—')).toHaveLength(3);
  });

  it('says so when the journal does not load', async () => {
    fakeApi(routes(() => journalOf(), { 'GET /api/idea-trades': () => problem(404, 'down') }));
    renderApp('/idea-trades');

    expect(await screen.findByText('Не удалось загрузить сделки.')).toBeInTheDocument();
  });

  it('leads to the settings and back', async () => {
    fakeApi(routes(() => journalOf()));
    const { history } = renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('link', { name: 'Настройки' }));

    expect(history.location.pathname).toBe('/idea-trades/settings');
    expect(await screen.findByRole('heading', { name: 'Торговля идеями' })).toBeInTheDocument();

    await user.click(within(screen.getByRole('main')).getByRole('link', { name: 'Сделки' }));

    expect(history.location.pathname).toBe('/idea-trades');
  });
});

describe('closing and cancelling in the group «В работе»', () => {
  // Scenario "Закрытие с подтверждением".
  it('closes an open trade by the market after the owner confirms, and shows the result', async () => {
    let closed = false;
    const requests = fakeApi(
      routes(
        () =>
          closed
            ? journalOf(
                [],
                [tradeOf({ status: 'closed_manually', result: '0.31', resultR: '0.3' })],
              )
            : journalOf([tradeOf({ status: 'open', openedAt: '2026-11-05T10:00:00Z' })]),
        {
          'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/close': () => {
            closed = true;

            return new Response(null, { status: 204 });
          },
        },
      ),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    const dialog = await screen.findByRole('dialog');
    expect(requests.some((request) => request.url.endsWith('/close'))).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть по рынку' }));

    expect(await screen.findByText('Закрыта вручную')).toBeInTheDocument();
    expect(screen.getByText('+0.31 USDT · +0.30 R')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('cancels the entry of a trade after the owner confirms', async () => {
    const requests = fakeApi(
      routes(open([tradeOf({})]), {
        'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/cancel': () =>
          new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Отменить' }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Отменить сделку' }),
    );

    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith('/cancel'))).toBe(true),
    );
  });

  it('shows the refusal of the API in the question and keeps it open', async () => {
    fakeApi(
      routes(open([tradeOf({ status: 'open', openedAt: '2026-11-05T10:00:00Z' })]), {
        'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/close': () =>
          problem(409, 'Only an open trade is closed.'),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть по рынку' }));

    expect(await within(dialog).findByText('Only an open trade is closed.')).toBeInTheDocument();
  });

  it('keeps the trade when the owner does not confirm', async () => {
    const requests = fakeApi(
      routes(open([tradeOf({ status: 'open', openedAt: '2026-11-05T10:00:00Z' })])),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Не закрывать' }),
    );

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });
});

describe('filters and pages of the journal', () => {
  // 120 closed trades, the page the request names.
  const paged = (total = 120) => {
    const answer = (request: Request) => {
      const query = new URL(request.url).searchParams;
      const size = Number(query.get('PageSize') ?? 50);
      const page = Math.min(Number(query.get('Page') ?? 1), Math.max(1, Math.ceil(total / size)));
      const from = (page - 1) * size;
      const items = Array.from({ length: Math.min(size, total - from) }, (_, index) =>
        tradeOf({
          id: `t${from + index + 1}`,
          symbol: `P${from + index + 1}USDT`,
          status: 'closed_at_target',
          result: '1',
          resultR: '1',
        }),
      );

      return json(journalOf([], items, { page, pageSize: size, total }));
    };

    return {
      'GET /api/idea-trades': answer,
      'GET /api/idea-trading/settings': () => json(tradingSettings),
    };
  };

  it('turns the pages of the closed trades with ›, writing the number into the address', async () => {
    const requests = fakeApi(paged());
    const { history } = renderApp('/idea-trades');
    expect(await screen.findByText('1–50 из 120')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Следующая страница' }));

    expect(await screen.findByText('51–100 из 120')).toBeInTheDocument();
    expect(lastQuery(requests)).toMatchObject({ Page: '2', PageSize: '50' });
    expect(history.location.search).toBe('?page=2');
  });

  // Scenario "Смена фильтра".
  it('goes back to the first page on a change of the badge, and asks for the badge', async () => {
    const requests = fakeApi(paged());
    const { history } = renderApp('/idea-trades?page=3');
    await screen.findByText('101–120 из 120');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Значок' }), 'up');

    await waitFor(() => expect(lastQuery(requests)).toMatchObject({ Badge: 'up', Page: '1' }));
    expect(history.location.search).toBe('');
  });

  it.each([
    ['Сетап', 'drop', { Setup: 'drop' }],
    ['Сторона', 'short', { Side: 'short' }],
    ['Значок', 'none', { Badge: 'none' }],
    ['Период', '7', { Days: '7' }],
    ['На странице', '100', { PageSize: '100' }],
  ])('asks the API for the choice of %s', async (name, value, expected) => {
    const requests = fakeApi(paged());
    renderApp('/idea-trades');
    await screen.findByText('1–50 из 120');

    await userEvent.selectOptions(screen.getByRole('combobox', { name }), value);

    await waitFor(() => expect(lastQuery(requests)).toMatchObject(expected));
  });

  it('asks for the pair typed after a pause', async () => {
    const requests = fakeApi(paged());
    renderApp('/idea-trades');
    await screen.findByText('1–50 из 120');

    await userEvent.type(screen.getByRole('textbox', { name: 'Пара' }), 'eth');

    await waitFor(() => expect(lastQuery(requests)).toMatchObject({ Symbol: 'eth' }));
  });

  // Scenario "Возврат на экран".
  it('keeps the period and the size of a page after the owner leaves the screen and comes back', async () => {
    fakeApi(paged());
    const first = renderApp('/idea-trades');
    await screen.findByText('1–50 из 120');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Период' }), '30');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '100');
    first.unmount();
    const requests = fakeApi(paged());

    renderApp('/idea-trades');

    await screen.findByText('1–100 из 120');
    expect(lastQuery(requests)).toMatchObject({ Days: '30', PageSize: '100' });
    expect(screen.getByRole('combobox', { name: 'Период' })).toHaveValue('30');
  });

  it('moves the address to the page the API gave when the one asked for is past the end', async () => {
    fakeApi(paged(80));
    const { history } = renderApp('/idea-trades?page=3');

    expect(await screen.findByText('51–80 из 80')).toBeInTheDocument();
    await waitFor(() => expect(history.location.search).toBe('?page=2'));
  });
});

const paperTrade = (overrides: Partial<Trade> = {}) =>
  tradeOf({
    id: 'p1',
    origin: 'paper',
    autoId: 'auto-3',
    autoName: 'Пробои 5м',
    symbol: 'DOGEUSDT',
    status: 'open',
    openedAt: '2026-11-07T12:50:00Z',
    ...overrides,
  });

const autoTrade = (overrides: Partial<Trade> = {}) =>
  tradeOf({
    id: 'a1',
    origin: 'auto',
    autoId: 'auto-1',
    autoName: 'Импульс лонг',
    symbol: 'AVAXUSDT',
    status: 'open',
    openedAt: '2026-11-07T12:31:00Z',
    ...overrides,
  });

describe('screen «Сделки»: who took the trade', () => {
  // Requirement "Таблица закрытых сделок", scenario "Кто взял".
  it('names who took each closed trade and marks the paper ones with a stripe', async () => {
    fakeApi(
      routes(() =>
        journalOf(
          [],
          [
            tradeOf({ id: 'm1', status: 'closed_at_stop', result: '-1', resultR: '-1' }),
            autoTrade({
              status: 'closed_at_target',
              result: '2',
              resultR: '1',
              statusAt: '2026-11-07T13:00:00Z',
            }),
            paperTrade({
              status: 'closed_at_target',
              result: '2',
              resultR: '1',
              statusAt: '2026-11-07T13:30:00Z',
            }),
          ],
        ),
      ),
    );
    renderApp('/idea-trades');

    const rows = within(await screen.findByRole('table', { name: 'Закрытые сделки' })).getAllByRole(
      'row',
    );
    const [manual, auto, paper] = [rows[1]!, rows[2]!, rows[3]!];

    expect(within(manual).getByText('Ручная')).toBeInTheDocument();
    expect(within(auto).getByText('Авто')).toBeInTheDocument();
    expect(within(auto).getByText('Импульс лонг')).toBeInTheDocument();
    expect(within(paper).getByText('учебная')).toBeInTheDocument();
    expect(within(paper).getByText('Пробои 5м')).toBeInTheDocument();
    expect(within(paper).getAllByRole('cell')[0]).toHaveClass('border-l-[3px]');
    expect(within(manual).getAllByRole('cell')[0]).not.toHaveClass('border-l-[3px]');
  });

  // Requirement "Группа «В работе»", scenario "Учебная открытая".
  it('shows «без ордеров» instead of the buttons of a paper trade in work, and the buttons of the real ones', async () => {
    fakeApi(routes(open([paperTrade(), autoTrade()])));
    renderApp('/idea-trades');

    const rows = within(await screen.findByRole('table', { name: 'Сделки в работе' })).getAllByRole(
      'row',
    );
    const paper = rows.find((row) => row.dataset.origin === 'paper')!;
    const auto = rows.find((row) => row.dataset.origin === 'auto')!;

    expect(within(paper).getByText('учебная')).toBeInTheDocument();
    expect(within(paper).getByText('Пробои 5м')).toBeInTheDocument();
    expect(within(paper).getByText('без ордеров')).toBeInTheDocument();
    expect(within(paper).queryByRole('button')).not.toBeInTheDocument();
    expect(within(auto).getByRole('button', { name: 'Закрыть по рынку' })).toBeInTheDocument();
    expect(within(auto).queryByText('без ордеров')).not.toBeInTheDocument();
  });

  // Requirement "Итоги на экране «Сделки»".
  it('shows two blocks of totals, with money and paper', async () => {
    fakeApi(
      routes(() =>
        journalOf([], [tradeOf({ status: 'closed_at_target', result: '1', resultR: '1' })], {
          totals: journalTotals({ targets: 1, stops: 2, meanR: '-0.26', result: '-1.34' }),
          paperTotals: journalTotals({ targets: 2, stops: 1, meanR: '0.93', result: '8.4' }),
        }),
      ),
    );
    renderApp('/idea-trades');

    const real = await screen.findByRole('region', { name: 'Итоги' });
    const paper = screen.getByRole('region', { name: 'Итоги учебных сделок' });

    expect(real).toHaveTextContent('С деньгами');
    expect(real).toHaveTextContent('ручные и автоматы · 3 сделки');
    expect(real).toHaveTextContent('-0.26 R');
    expect(paper).toHaveTextContent('Учебные');
    expect(paper).toHaveTextContent('без ордеров · 3 сделки · в защиты не входят');
    expect(paper).toHaveTextContent('+0.93 R');
  });

  // Scenario "Только учебные".
  it('shows only the paper totals when the owner chose «Учебные»', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades');
    const user = userEvent.setup();
    await screen.findByRole('region', { name: 'Итоги' });

    await user.click(screen.getByRole('radio', { name: 'Учебные' }));

    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Итоги' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('region', { name: 'Итоги учебных сделок' })).toBeInTheDocument();
  });

  it('shows only the totals with money when the owner chose the autos or the hand', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades');
    const user = userEvent.setup();
    await screen.findByRole('region', { name: 'Итоги' });

    await user.click(screen.getByRole('radio', { name: 'Авто' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('region', { name: 'Итоги учебных сделок' }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('region', { name: 'Итоги' })).toHaveTextContent('автоматы');
  });

  // Requirement "Фильтры и листание сделок".
  it('asks for the trades of the kind chosen and keeps the choice in the browser', async () => {
    const requests = fakeApi(routes(() => journalOf()));
    const first = renderApp('/idea-trades');
    const user = userEvent.setup();
    await screen.findByRole('region', { name: 'Итоги' });

    await user.click(screen.getByRole('radio', { name: 'Учебные' }));

    await waitFor(() => expect(lastQuery(requests)).toMatchObject({ Origin: 'paper' }));
    expect(screen.getByRole('radio', { name: 'Учебные' })).toBeChecked();
    first.unmount();
    fakeApi(routes(() => journalOf()));

    renderApp('/idea-trades');

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Учебные' })).toBeChecked());
  });

  // Scenario "Один автомат".
  it('filters the journal and the refusals by one auto', async () => {
    const requests = fakeApi(
      routes(() => journalOf(), {
        'GET /api/idea-autos': () =>
          json([
            autoItemOf(autoOf({ id: 'auto-1', name: 'Импульс лонг' })),
            autoItemOf(autoOf({ id: 'auto-2', name: 'Падения шорт' })),
          ]),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    const select = await screen.findByRole('combobox', { name: 'Автомат' });
    await screen.findByRole('option', { name: 'Импульс лонг' });

    await user.selectOptions(select, 'auto-1');

    await waitFor(() => expect(lastQuery(requests)).toMatchObject({ AutoId: 'auto-1' }));
    await waitFor(() =>
      expect(
        requests.some(
          (request) => request.url.includes('/refusals') && request.url.includes('autoId=auto-1'),
        ),
      ).toBe(true),
    );
  });

  it('opens on the auto of the address and moves the address to the journal as it was', async () => {
    const requests = fakeApi(routes(() => journalOf()));
    const { history } = renderApp('/idea-trades?autoId=auto-1');

    await waitFor(() => expect(lastQuery(requests)).toMatchObject({ AutoId: 'auto-1' }));
    await waitFor(() => expect(history.location.search).toBe(''));
  });

  it('leads to the autos', async () => {
    fakeApi(routes(() => journalOf()));
    const { history } = renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('link', { name: 'Автоматы' }));

    expect(history.location.pathname).toBe('/idea-trades/autos');
  });
});

describe('block «Отказы автоматов»', () => {
  // Requirement "Блок «Отказы автоматов»", scenario "Отказ из-за занятой пары".
  it('shows the refusals with the idea, the pair, the reason and what exactly stopped the auto', async () => {
    fakeApi(
      routes(() => journalOf(), {
        'GET /api/idea-autos/refusals': () =>
          json(
            refusalsPage([
              refusalOf(),
              refusalOf({
                id: 'r2',
                at: '2026-11-07T10:15:00Z',
                autoName: 'Импульс лонг',
                symbol: 'APTUSDT',
                setup: 'impulse',
                interval: '15',
                side: 'long',
                reasons: ['not_enough_money'],
                detail: { actual: '212.4', limit: '300', holder: null },
              }),
              refusalOf({
                id: 'r3',
                autoName: 'Пробои 5м',
                mode: 'paper',
                reasons: ['late'],
                detail: null,
                symbol: 'WIFUSDT',
              }),
            ]),
          ),
      }),
    );
    renderApp('/idea-trades');

    const table = await screen.findByRole('table', { name: 'Отказы автоматов' });
    const rows = within(table).getAllByRole('row');

    expect(within(rows[1]!).getByText('12:30')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Падения шорт')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('с деньгами')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Падение · 1 ч · Цель 2R · Шорт')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('SUIUSDT')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Уже есть сделка по паре')).toBeInTheDocument();
    expect(
      within(rows[1]!).getByText('на Bybit · ideas её взял «Импульс лонг» — он выше в списке'),
    ).toBeInTheDocument();
    expect(within(rows[2]!).getByText('Не хватает денег')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('свободно 212.40 из 300 USDT')).toBeInTheDocument();
    expect(within(rows[3]!).getByText('без денег')).toBeInTheDocument();
    expect(within(rows[3]!).getByText('Не успел войти')).toBeInTheDocument();
    expect(screen.getByText(/сегодня 3 · с 03:00 ·/)).toBeInTheDocument();
  });

  // Scenario "Отказов нет".
  it('says there were no refusals today', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades');

    expect(await screen.findByText('Сегодня отказов не было.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Отказы автоматов' })).not.toBeInTheDocument();
  });

  it('asks for the refusals of the last seven days when the owner switches, and shows the day with the time', async () => {
    const requests = fakeApi(
      routes(() => journalOf(), {
        'GET /api/idea-autos/refusals': () => json(refusalsPage([refusalOf()])),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    await screen.findByRole('table', { name: 'Отказы автоматов' });

    await user.click(screen.getByRole('radio', { name: '7 дней' }));

    await waitFor(() =>
      expect(requests.some((request) => request.url.includes('period=7d'))).toBe(true),
    );
    expect(await screen.findByText('07.11 12:30')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '7 дней' })).toBeChecked();
  });

  it('turns the pages of the refusals when there are more than a page', async () => {
    const requests = fakeApi(
      routes(() => journalOf(), {
        'GET /api/idea-autos/refusals': () => json({ ...refusalsPage([refusalOf()]), total: 120 }),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    await screen.findByRole('table', { name: 'Отказы автоматов' });

    await user.click(screen.getByRole('button', { name: 'Следующая страница' }));

    await waitFor(() =>
      expect(requests.some((request) => request.url.includes('page=2'))).toBe(true),
    );
  });

  it('says so when the refusals do not load', async () => {
    fakeApi(
      routes(() => journalOf(), { 'GET /api/idea-autos/refusals': () => problem(404, 'down') }),
    );
    renderApp('/idea-trades');

    expect(await screen.findByText('Не удалось загрузить отказы.')).toBeInTheDocument();
  });
});

describe('banner of the daily stop on «Сделки»', () => {
  // Requirement "Баннер дневного стопа", scenario "Стоп сработал".
  it('shows the sum of the day against the limit and when the stop held', async () => {
    fakeApi(
      routes(() => journalOf(), {
        'GET /api/idea-trading/guards': () =>
          json(
            guardsOf({
              daySum: '-30.15',
              triggeredAt: '2026-11-07T15:12:00Z',
              resumesAt: '2026-11-08T00:00:00Z',
            }),
          ),
      }),
    );
    renderApp('/idea-trades');

    const banner = await screen.findByRole('status', { name: 'Дневной стоп' });

    expect(banner).toHaveTextContent('Сегодня -30.15 из 30 USDT · сработал в 18:12 ·');
  });

  // Scenario "Стоп не сработал".
  it('shows no banner when the stop has not held', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades');
    await screen.findByRole('region', { name: 'Итоги' });

    expect(screen.queryByRole('status', { name: 'Дневной стоп' })).not.toBeInTheDocument();
  });
});
