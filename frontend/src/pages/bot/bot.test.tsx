import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import {
  backtestSummary,
  botDetails,
  botId,
  botSummary,
  emaDefinition,
  emaSummary,
  exchangeAccount,
  progressOf,
  savedStrategy,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

interface Hub {
  onSnapshot: (snapshot: unknown) => void;
  onCandle: (candle: unknown) => void;
  onLost: () => void;
}

// The channel is driven by hand: the test keeps the handlers the chart gave to the hub.
const hub = vi.hoisted(() => ({ handlers: undefined as unknown as Hub }));

vi.mock('@/modules/bots/realtime/followFormingCandle', () => ({
  followFormingCandle: (_botId: string, handlers: Hub) => {
    hub.handlers = handlers;

    return () => undefined;
  },
}));

function routes(backtests: () => unknown[]) {
  return {
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    [`GET /api/bots/${botId}/backtests`]: () => json(backtests()),
    [`GET /api/bots/${botId}/positions`]: () => json([]),
    [`GET /api/bots/${botId}/candles`]: () =>
      json({ interval: '60', candles: [], unavailable: false }),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, emaDefinition)),
    'GET /api/market-data/instruments': () =>
      json([{ category: 'linear', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' }]),
  };
}

describe('bot page', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows the settings of the bot with the quote coin of the balance', async () => {
    fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('heading', { name: 'BTC EMA час' })).toBeInTheDocument();
    expect(
      await screen.findByText('EMA · linear · BTCUSDT · 1 ч · баланс 1 000 USDT'),
    ).toBeInTheDocument();
  });

  // Scenario "Бот с аккаунтом".
  it('shows the account of the bot with a link to it', async () => {
    fakeApi({
      ...routes(() => []),
      [`GET /api/bots/${botId}`]: () =>
        json({
          ...botDetails,
          exchangeAccount: { id: exchangeAccount.id, name: 'Проверка' },
        }),
    });
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('link', { name: 'Проверка' })).toHaveAttribute(
      'href',
      `/accounts/${exchangeAccount.id}`,
    );
    expect(screen.queryByText('Testnet')).not.toBeInTheDocument();
  });

  // Scenario "Бот без аккаунта".
  it('says that the bot has no account', async () => {
    fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    expect(await screen.findByText(/Без аккаунта/)).toBeInTheDocument();
  });

  // Scenario "Значения параметров".
  it('marks the parameters that take their default', async () => {
    fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    const parameters = await screen.findByRole('region', { name: 'Параметры' });
    const fast = (await within(parameters).findByText('fast')).closest('li')!;
    expect(within(fast).getByText('10')).toBeInTheDocument();
    expect(within(fast).queryByText('по умолчанию')).not.toBeInTheDocument();
    const slow = within(parameters).getByText('slow').closest('li')!;
    expect(within(slow).getByText('26')).toBeInTheDocument();
    expect(within(slow).getByText('по умолчанию')).toBeInTheDocument();
  });

  it('lists backtests with period, ranges and combinations', async () => {
    fakeApi(
      routes(() => [
        backtestSummary({
          parameters: { fast: { min: 5, max: 15, step: 5 } },
          progress: progressOf(3, 3),
        }),
      ]),
    );
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 17:05')).closest('tr')!;
    expect(within(row).getByText('01.01.2024 – 01.07.2024')).toBeInTheDocument();
    expect(within(row).getByText('fast 5…15 / 5')).toBeInTheDocument();
    expect(within(row).getByText('3')).toBeInTheDocument();
    expect(within(row).getByText('Готов')).toBeInTheDocument();
  });

  it('shows a dash where no parameter is a range', async () => {
    fakeApi(routes(() => [backtestSummary()]));
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 17:05')).closest('tr')!;
    expect(within(row).getByText('—')).toBeInTheDocument();
  });

  // Scenario "Ошибка бэктеста".
  it('shows the reason a backtest failed', async () => {
    fakeApi(
      routes(() => [backtestSummary({ status: 'Failed', error: 'Bybit: биржа недоступна' })]),
    );
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 17:05')).closest('tr')!;
    expect(within(row).getByText('Ошибка')).toBeInTheDocument();
    expect(within(row).getByText('Bybit: биржа недоступна')).toBeInTheDocument();
  });

  it('shows how many combinations a cancelled backtest calculated', async () => {
    fakeApi(routes(() => [backtestSummary({ status: 'Cancelled', progress: progressOf(7, 19) })]));
    renderApp(`/bots/${botId}`);

    expect(await screen.findByText('Отменён · 7 / 19')).toBeInTheDocument();
  });

  // Scenario "Бот изменился".
  it('marks a backtest that was run for another bot', async () => {
    fakeApi(
      routes(() => [
        backtestSummary({ botChanged: true }),
        backtestSummary({ id: 'other', createdAt: '2026-11-01T10:00:00Z' }),
      ]),
    );
    renderApp(`/bots/${botId}`);

    const changed = (await screen.findByText('02.11 17:05')).closest('tr')!;
    expect(within(changed).getByText('бот изменился')).toBeInTheDocument();
    const other = screen.getByText('01.11 13:00').closest('tr')!;
    expect(within(other).queryByText('бот изменился')).not.toBeInTheDocument();
  });

  // Scenario "Ход расчёта обновляется".
  it('polls every 2 seconds while a backtest is unfinished and stops when it is done', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let calculated = 5;
    let status = 'Running';
    const requests = fakeApi(
      routes(() => [backtestSummary({ status, progress: progressOf(calculated, 9) })]),
    );
    renderApp(`/bots/${botId}`);

    expect(await screen.findByText('Считается · 5 / 9')).toBeInTheDocument();
    calculated = 7;
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Считается · 7 / 9')).toBeInTheDocument();

    status = 'Succeeded';
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Готов')).toBeInTheDocument();

    const listed = () => requests.filter((request) => request.url.endsWith('/backtests')).length;
    const before = listed();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(listed()).toBe(before);
  });

  // Design decision 3: a finished backtest changes what the bots list shows.
  it('refreshes the bots list and the backtests of the bot when a backtest finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let status = 'Running';
    const requests = fakeApi({
      ...routes(() => [backtestSummary({ status, progress: progressOf(5, 9) })]),
      'GET /api/bots': () => json([botSummary]),
    });
    const { history } = renderApp('/bots');
    await screen.findByText('BTC EMA час');
    act(() => history.push(`/bots/${botId}`));
    expect(await screen.findByText('Считается · 5 / 9')).toBeInTheDocument();

    status = 'Succeeded';
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Готов')).toBeInTheDocument();
    const listed = () => requests.filter((request) => request.url.endsWith('/api/bots')).length;
    const before = listed();
    act(() => history.push('/bots'));

    await waitFor(() => expect(listed()).toBe(before + 1));
  });

  it('shows a progress bar in the row of a running backtest', async () => {
    fakeApi(routes(() => [backtestSummary({ status: 'Running', progress: progressOf(5, 10) })]));
    renderApp(`/bots/${botId}`);

    const bar = await screen.findByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '50');
  });

  // Scenario "Подтверждённое удаление".
  it('deletes the bot after the confirmation and opens the list', async () => {
    let deleted = false;
    const requests = fakeApi({
      ...routes(() => [backtestSummary(), backtestSummary({ id: 'second' })]),
      [`DELETE /api/bots/${botId}`]: () => {
        deleted = true;

        return new Response(null, { status: 204 });
      },
      'GET /api/bots': () => json(deleted ? [] : [botDetails]),
    });
    const { history } = renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Вместе с ботом удаляются все его бэктесты.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Удалить' }));

    await waitFor(() => expect(history.location.pathname).toBe('/bots'));
    expect(requests.some((request) => request.method === 'DELETE')).toBe(true);
    expect(await screen.findByRole('heading', { name: 'Боты' })).toBeInTheDocument();
    expect(screen.queryByText('BTC EMA час')).not.toBeInTheDocument();
  });

  it('keeps the bot when the deletion is declined', async () => {
    const requests = fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Отмена' }),
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'DELETE')).toBe(false);
  });

  it('says when the bot cannot be loaded', async () => {
    fakeApi({ ...routes(() => []), [`GET /api/bots/${botId}`]: () => problem(404, 'нет') });
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бота.');
  });

  it('says when the backtests cannot be loaded and still shows the bot', async () => {
    fakeApi({
      ...routes(() => []),
      [`GET /api/bots/${botId}/backtests`]: () => problem(404, 'нет'),
    });
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бэктесты.');
    expect(screen.getByRole('heading', { name: 'BTC EMA час' })).toBeInTheDocument();
  });

  it('shows why the bot was not deleted and stays on the page', async () => {
    fakeApi({
      ...routes(() => []),
      [`DELETE /api/bots/${botId}`]: () => problem(409, 'У бота есть бэктест в работе.'),
    });
    const { history } = renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('У бота есть бэктест в работе.');
    expect(history.location.pathname).toBe(`/bots/${botId}`);
  });
});

describe('chart on the bot page', () => {
  const candle = (openTime: string, close = '105', volume = '7') => ({
    openTime,
    open: '100',
    high: '110',
    low: '95',
    close,
    volume,
    turnover: '0',
  });
  const closed = [
    candle('2026-11-03T09:00:00Z', '101'),
    candle('2026-11-03T10:00:00Z', '102', '12.5'),
  ];
  const fill = (at: string, price: string) => ({
    at,
    price,
    quantity: '0.5',
    fee: '0',
    feeAsset: 'quote',
  });
  const order = (overrides: object = {}) => ({
    number: 1,
    node: 'entry',
    side: 'buy',
    type: 'market',
    price: null,
    quantity: '0.5',
    amount: null,
    status: 'filled',
    filledQuantity: '0.5',
    averagePrice: '100',
    fee: '0',
    rejectReason: null,
    fills: [fill('2026-11-03T10:15:00Z', '100')],
    ...overrides,
  });
  const position = (orders: object[]) => ({
    number: 1,
    status: 'open',
    exitSide: 'sell',
    openedAt: '2026-11-03T10:15:00Z',
    closedAt: null,
    quantity: '0.5',
    invested: '50',
    entryPrice: '100',
    exitPrice: null,
    pnl: '0',
    fees: '0',
    orders,
  });

  function open(path = `/bots/${botId}`, extra: Record<string, FakeRoute> = {}) {
    const requests = fakeApi({
      ...routes(() => []),
      [`GET /api/bots/${botId}/candles`]: () =>
        json({ interval: '60', candles: closed, unavailable: false }),
      ...extra,
    });
    const rendered = renderApp(path);

    return { requests, ...rendered };
  }

  const candleRequests = (requests: Request[]) =>
    requests.filter((request) => new URL(request.url).pathname === `/api/bots/${botId}/candles`);
  const lastCandle = () => screen.findByRole('group', { name: 'Последняя свеча' });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "График остановленного бота".
  it('draws the candles of the bot on its interval and describes the last closed one', async () => {
    const { requests } = open();

    const last = await lastCandle();
    await waitFor(() => expect(last).toHaveTextContent('2026-11-03 13:00'));
    expect(last).toHaveTextContent('102');
    expect(last).toHaveTextContent('12.5');
    expect(last).not.toHaveTextContent('не закрыта');
    expect(
      screen.getByRole('img', { name: 'График свечей BTCUSDT, 1 ч, исполнений: 0, ордеров: 0' }),
    ).toBeInTheDocument();
    expect(new URL(candleRequests(requests)[0]!.url).searchParams.get('Interval')).toBe('60');
  });

  it('offers the intervals with the one of the bot marked', async () => {
    open();

    const group = await screen.findByRole('group', { name: 'Интервал' });

    expect(
      within(group)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['1 мин', '5 мин', '15 мин', '1 ч · бот', '4 ч', '1 д']);
    expect(within(group).getByRole('button', { name: '1 ч · бот' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  // Scenarios "Цена меняется" and "Состояние канала текущей свечи".
  it('shows the current candle as not closed and the state of the channel', async () => {
    open();
    await lastCandle();
    expect(screen.getByText('подключение')).toBeInTheDocument();

    act(() =>
      hub.handlers.onSnapshot({
        state: 'live',
        candle: { ...candle('2026-11-03T11:00:00Z', '103', '3.25'), isClosed: false },
      }),
    );

    const last = await lastCandle();
    expect(last).toHaveTextContent('2026-11-03 14:00');
    expect(last).toHaveTextContent('103');
    expect(last).toHaveTextContent('3.25');
    expect(last).toHaveTextContent('не закрыта');
    expect(screen.getByText('в реальном времени')).toBeInTheDocument();

    act(() => hub.handlers.onLost());
    expect(screen.getByText('нет данных')).toBeInTheDocument();
    expect(last).toHaveTextContent('не закрыта');
  });

  // Scenario "Свеча закрылась".
  it('shows the candle once, closed, when the history has it', async () => {
    open();
    await lastCandle();

    act(() => hub.handlers.onCandle({ ...candle('2026-11-03T10:00:00Z', '999'), isClosed: true }));

    const last = await lastCandle();
    expect(last).toHaveTextContent('102');
    expect(last).not.toHaveTextContent('999');
    expect(last).not.toHaveTextContent('не закрыта');
  });

  // Scenario "Выбор интервала" and "Дневной график часового бота".
  it('switches to another interval, writes it to the address and drops the current candle', async () => {
    const { requests, history } = open();
    await lastCandle();
    act(() =>
      hub.handlers.onSnapshot({
        state: 'live',
        candle: { ...candle('2026-11-03T11:00:00Z'), isClosed: false },
      }),
    );

    await userEvent.click(screen.getByRole('button', { name: '1 д' }));

    await waitFor(() => expect(history.location.search).toBe('?interval=D'));
    await waitFor(() =>
      expect(
        candleRequests(requests).some(
          (request) => new URL(request.url).searchParams.get('Interval') === 'D',
        ),
      ).toBe(true),
    );
    expect(screen.getByRole('button', { name: '1 д' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText(/раз в минуту/)).toBeInTheDocument();
    expect(screen.queryByText('в реальном времени')).not.toBeInTheDocument();
    expect(await lastCandle()).not.toHaveTextContent('не закрыта');
  });

  it('writes a numeric interval to the address as it is', async () => {
    const { history } = open();
    await lastCandle();

    await userEvent.click(screen.getByRole('button', { name: '5 мин' }));

    await waitFor(() => expect(history.location.search).toBe('?interval=5'));
  });

  it('keeps the interval of the bot out of the address', async () => {
    const { history } = open(`/bots/${botId}?interval=5`);
    await lastCandle();

    await userEvent.click(screen.getByRole('button', { name: '1 ч · бот' }));

    await waitFor(() => expect(history.location.search).toBe(''));
  });

  // Scenario "Ссылка на интервал".
  it('opens the interval of the address', async () => {
    const { requests } = open(`/bots/${botId}?interval=5`);

    await lastCandle();

    expect(screen.getByRole('button', { name: '5 мин' })).toHaveAttribute('aria-pressed', 'true');
    expect(new URL(candleRequests(requests)[0]!.url).searchParams.get('Interval')).toBe('5');
  });

  it('opens the interval of the bot for an interval the chart does not offer', async () => {
    const { requests } = open(`/bots/${botId}?interval=7`);

    await lastCandle();

    expect(new URL(candleRequests(requests)[0]!.url).searchParams.get('Interval')).toBe('60');
  });

  // Scenarios "Исполнение вне нарисованных свечей", "Стоящая лимитная продажа".
  it('counts the fills on drawn candles and the active limit orders in the description', async () => {
    open(`/bots/${botId}`, {
      [`GET /api/bots/${botId}/positions`]: () =>
        json([
          position([
            order(),
            order({ number: 2, fills: [fill('2026-11-02T10:15:00Z', '99')] }),
            order({
              number: 3,
              side: 'sell',
              type: 'limit',
              price: '105000',
              quantity: '0.01',
              status: 'active',
              filledQuantity: '0',
              fills: [],
            }),
            order({
              number: 4,
              side: 'sell',
              type: 'limit',
              price: '1',
              status: 'cancelled',
              fills: [],
            }),
          ]),
        ]),
    });

    expect(
      await screen.findByRole('img', {
        name: 'График свечей BTCUSDT, 1 ч, исполнений: 1, ордеров: 1',
      }),
    ).toBeInTheDocument();
  });

  // Scenario "Ордер исполнился": the chart follows the polling of the page.
  it('shows a new fill with the next poll of the positions', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let orders: object[] = [];
    open(`/bots/${botId}`, {
      [`GET /api/bots/${botId}`]: () => json({ ...botDetails, status: 'running' }),
      [`GET /api/bots/${botId}/positions`]: () => json([position(orders)]),
    });
    await screen.findByRole('img', { name: /исполнений: 0/ });

    orders = [order()];
    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(await screen.findByRole('img', { name: /исполнений: 1/ })).toBeInTheDocument();
  });

  // Scenario "Bybit не отвечает".
  it('says the history is unavailable and keeps the status and the position', async () => {
    open(`/bots/${botId}`, {
      [`GET /api/bots/${botId}/candles`]: () =>
        json({ interval: '60', candles: closed, unavailable: true }),
      [`GET /api/bots/${botId}/positions`]: () => json([position([order()])]),
    });

    expect(await screen.findByText('История недоступна: Bybit не отвечает')).toBeInTheDocument();
    expect(screen.getByText('Остановлен')).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Текущая позиция' })).toBeInTheDocument();
    expect(await lastCandle()).toHaveTextContent('102');
  });

  it('says when the candles cannot be loaded and keeps the bot', async () => {
    open(`/bots/${botId}`, {
      [`GET /api/bots/${botId}/candles`]: () => problem(400, 'сбой'),
    });

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить свечи.');
    expect(screen.getByRole('heading', { name: 'BTC EMA час' })).toBeInTheDocument();
  });

  it('says there are no candles when the instrument has none', async () => {
    open(`/bots/${botId}`, {
      [`GET /api/bots/${botId}/candles`]: () =>
        json({ interval: '60', candles: [], unavailable: false }),
    });

    expect(await screen.findByText('Свечей за период нет.')).toBeInTheDocument();
  });
});
