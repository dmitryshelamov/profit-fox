import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import {
  botDetails,
  botId,
  emaDefinition,
  emaSummary,
  exchangeAccount,
  savedStrategy,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const account = { id: exchangeAccount.id, name: 'Основной' };

function position(overrides: object = {}) {
  return {
    number: 1,
    status: 'open',
    exitSide: 'sell',
    openedAt: '2026-11-03T10:00:00Z',
    closedAt: null,
    quantity: '0.5',
    invested: '50',
    entryPrice: '100',
    exitPrice: null,
    pnl: '0',
    fees: '0.05',
    orders: [
      {
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
        fee: '0.05',
        rejectReason: null,
        fills: [
          {
            at: '2026-11-03T10:00:00Z',
            price: '100',
            quantity: '0.5',
            fee: '0.05',
            feeAsset: 'quote',
          },
        ],
      },
      {
        number: 2,
        node: 'exit',
        side: 'sell',
        type: 'limit',
        price: '101',
        quantity: '0.5',
        amount: null,
        status: 'active',
        filledQuantity: '0',
        averagePrice: null,
        fee: '0',
        rejectReason: null,
        fills: [],
      },
    ],
    ...overrides,
  };
}

function open(bot: object, positions: unknown[] = [], extra: Record<string, FakeRoute> = {}) {
  const requests = fakeApi({
    [`GET /api/bots/${botId}`]: () => json({ ...botDetails, exchangeAccount: account, ...bot }),
    [`GET /api/bots/${botId}/backtests`]: () => json([]),
    [`GET /api/bots/${botId}/positions`]: () => json(positions),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, emaDefinition)),
    'GET /api/market-data/instruments': () =>
      json([{ category: 'linear', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' }]),
    ...extra,
  });
  renderApp(`/bots/${botId}`);

  return requests;
}

describe('live trading on the bot page', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Действия запущенного бота" and requirement "Правка недоступна у работающего бота".
  it('offers pause and stop to a running bot and no changes', async () => {
    open({ status: 'running' });

    expect(await screen.findByRole('button', { name: 'Пауза' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Остановить' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Запустить' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Закрыть позицию' })).not.toBeInTheDocument();
    expect(screen.getByText('Запущен')).toBeInTheDocument();
    expect(
      screen.getByText('Остановите бота, чтобы изменить или удалить его.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Изменить' })).not.toBeInTheDocument();
  });

  it('offers resume and stop to a paused bot', async () => {
    open({ status: 'paused' });

    expect(await screen.findByRole('button', { name: 'Продолжить' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Остановить' })).toBeEnabled();
  });

  // Scenario "Действия запущенного бота": each action sends its own request.
  it.each([
    ['running', 'Пауза', 'pause'],
    ['paused', 'Продолжить', 'resume'],
    ['running', 'Остановить', 'stop'],
  ])('a %s bot sends %s as its request', async (status, button, action) => {
    const requests = open({ status }, [], {
      [`POST /api/bots/${botId}/${action}`]: () => new Response(null, { status: 204 }),
    });

    await userEvent.click(await screen.findByRole('button', { name: button }));

    await waitFor(() =>
      expect(
        requests
          .filter((request) => request.method === 'POST')
          .map((request) => new URL(request.url).pathname),
      ).toEqual([`/api/bots/${botId}/${action}`]),
    );
  });

  // A second click while the first action is on its way sends nothing more.
  it('sends an action once while it is on its way', async () => {
    let answer = () => {};
    const requests = open({ status: 'running' }, [], {
      [`POST /api/bots/${botId}/stop`]: () =>
        new Promise<Response>((resolve) => {
          answer = () => resolve(new Response(null, { status: 204 }));
        }),
    });
    const stop = await screen.findByRole('button', { name: 'Остановить' });

    await userEvent.click(stop);
    await userEvent.click(stop);
    answer();

    await waitFor(() => expect(stop).toBeEnabled());
    expect(requests.filter((request) => request.method === 'POST')).toHaveLength(1);
  });

  it('sends nothing when the owner cancels the confirmation', async () => {
    const requests = open({ status: 'stopped' }, [position({ orders: [] })]);

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть позицию' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Отмена' }),
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  it('shows why the server refused to close the position', async () => {
    open({ status: 'stopped' }, [position({ orders: [] })], {
      [`POST /api/bots/${botId}/close-position`]: () =>
        problem(409, 'The bot has no open position.'),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть позицию' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Закрыть позицию' }),
    );

    expect(await screen.findByText('The bot has no open position.')).toBeInTheDocument();
  });

  // A stopping bot waits for its cancels: nothing is offered meanwhile.
  it('offers nothing to a stopping bot', async () => {
    open({ status: 'stopping' }, [position({ orders: [] })]);

    expect(await screen.findByText('Останавливается')).toBeInTheDocument();
    for (const name of ['Запустить', 'Пауза', 'Продолжить', 'Остановить', 'Закрыть позицию']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    }
  });

  // A remainder below the minimum is no open position: there is nothing to close.
  it('offers no close for a position that waits for its entry', async () => {
    open({ status: 'stopped' }, [position({ status: 'waiting', quantity: '0.0001', orders: [] })]);

    expect(await screen.findByRole('button', { name: 'Запустить' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Закрыть позицию' })).not.toBeInTheDocument();
  });

  // Requirement "Опрос статуса": the page reads the bot every 5 seconds while it works, and stops once it is stopped.
  it('reads a working bot every 5 seconds and a stopped one no more', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let status = 'running';
    const requests = open({}, [], {
      [`GET /api/bots/${botId}`]: () => json({ ...botDetails, exchangeAccount: account, status }),
    });
    await screen.findByText('Запущен');
    const reads = () =>
      requests.filter(
        (request) => request.method === 'GET' && request.url.endsWith(`/api/bots/${botId}`),
      ).length;
    const first = reads();

    status = 'stopped';
    await act(() => vi.advanceTimersByTimeAsync(5000));
    await screen.findByText('Остановлен');
    const after = reads();
    await act(() => vi.advanceTimersByTimeAsync(15000));

    expect(after).toBeGreaterThan(first);
    expect(reads()).toBe(after);
  });

  // A bot with orders on the exchange that no Worker takes up stays running: the page tells why.
  it('shows why a working bot is not taken up', async () => {
    open({
      status: 'running',
      statusReason: 'The Worker failed to take the bot 3 times in a row and keeps trying.',
    });

    expect(
      await screen.findByText(
        'The Worker failed to take the bot 3 times in a row and keeps trying.',
      ),
    ).toBeInTheDocument();
  });

  // Scenario "Причина ошибки".
  it('shows the reason of a failed bot', async () => {
    open({ status: 'failed', statusReason: 'Insufficient balance.' });

    expect(await screen.findByText('Ошибка')).toBeInTheDocument();
    expect(screen.getByText('Insufficient balance.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Запустить' })).toBeEnabled();
  });

  // Requirement "Подтверждение действий с деньгами": the start names the account, the instrument and the balance.
  it('starts a bot after the owner confirms what it trades', async () => {
    const requests = open({ status: 'stopped' }, [], {
      [`POST /api/bots/${botId}/start`]: () => new Response(null, { status: 204 }),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Основной/)).toBeInTheDocument();
    expect(within(dialog).getByText(/linear · BTCUSDT/)).toBeInTheDocument();
    expect(within(dialog).getByText(/1 000 USDT/)).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Запустить' }));

    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith(`/api/bots/${botId}/start`))).toBe(
        true,
      ),
    );
  });

  // The confirmation is modal: the focus moves into it, and Escape steps back without the action.
  it('asks in a modal dialog that Escape closes without acting', async () => {
    const requests = open({ status: 'stopped' });

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toContainElement(document.activeElement as HTMLElement);

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  // Scenario "Запуск без денег".
  it('shows why the server refused the start', async () => {
    open({ status: 'stopped' }, [], {
      [`POST /api/bots/${botId}/start`]: () =>
        problem(409, 'Not enough free money on the account: 500 USDT.'),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Запустить' }),
    );

    expect(
      await screen.findByText('Not enough free money on the account: 500 USDT.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Остановлен')).toBeInTheDocument();
  });

  // Scenario "Подтверждение закрытия".
  it('closes the position after the owner confirms the market order', async () => {
    const requests = open({ status: 'stopped' }, [position({ orders: [] })], {
      [`POST /api/bots/${botId}/close-position`]: () => new Response(null, { status: 204 }),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть позицию' }));

    expect(
      within(screen.getByRole('dialog')).getByText('Продажа по рынку 0.5 BTC.'),
    ).toBeInTheDocument();
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Закрыть позицию' }),
    );
    await waitFor(() =>
      expect(
        requests.some((request) => request.url.endsWith(`/api/bots/${botId}/close-position`)),
      ).toBe(true),
    );
  });

  // The side comes with the position from the version the bot trades, though the strategy is edited since.
  it('names the exit side of the version the bot trades', async () => {
    open({ status: 'stopped' }, [position({ exitSide: 'buy', orders: [] })]);

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть позицию' }));

    expect(
      within(screen.getByRole('dialog')).getByText('Покупка по рынку 0.5 BTC.'),
    ).toBeInTheDocument();
  });

  it('tells that the position closes and offers nothing meanwhile', async () => {
    open({ status: 'stopped', closeRequested: true }, [position({ orders: [] })]);

    expect(await screen.findByText('Позиция закрывается по рынку…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Запустить' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Закрыть позицию' })).not.toBeInTheDocument();
  });

  // A Worker may close the position between two polls: the positions are read again once the bot is no longer followed.
  it('reads the positions again when the bot is done closing', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let closing = true;
    open({ status: 'stopped', closeRequested: true }, [], {
      [`GET /api/bots/${botId}`]: () =>
        json({
          ...botDetails,
          exchangeAccount: account,
          status: 'stopped',
          closeRequested: closing,
        }),
      [`GET /api/bots/${botId}/positions`]: () =>
        json(
          closing
            ? [position({ orders: [] })]
            : [
                position({ number: 2, status: 'waiting', quantity: '0', orders: [] }),
                position({ status: 'closed', closedAt: '2026-11-03T12:00:00Z', orders: [] }),
              ],
        ),
    });
    expect(await screen.findByText('Позиция закрывается по рынку…')).toBeInTheDocument();

    closing = false;
    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(await screen.findByRole('region', { name: 'Закрытые позиции' })).toBeInTheDocument();
  });

  it('tells that the positions did not load', async () => {
    open({ status: 'running' }, [], {
      [`GET /api/bots/${botId}/positions`]: () => problem(503, 'Boom.'),
    });

    expect(
      await screen.findByText('Не удалось загрузить позиции.', {}, { timeout: 3000 }),
    ).toHaveAttribute('role', 'alert');
  });

  // A refused action may mean a Worker moved the bot on meanwhile: the status is read again.
  it('reads the status again after a refused action', async () => {
    const requests = open({ status: 'running' }, [], {
      [`POST /api/bots/${botId}/pause`]: () => problem(409, 'Only a running bot pauses.'),
    });
    const reads = () =>
      requests.filter(
        (request) => request.method === 'GET' && request.url.endsWith(`/api/bots/${botId}`),
      ).length;
    const pause = await screen.findByRole('button', { name: 'Пауза' });
    const before = reads();

    await userEvent.click(pause);

    expect(await screen.findByText('Only a running bot pauses.')).toBeInTheDocument();
    await waitFor(() => expect(reads()).toBeGreaterThan(before));
  });

  // Without an answer the result of the action is unknown: the owner is told so, in Russian.
  it('tells that the server did not answer an action', async () => {
    open({ status: 'running' }, [], {
      [`POST /api/bots/${botId}/stop`]: () => {
        throw new TypeError('Failed to fetch');
      },
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Остановить' }));

    expect(
      await screen.findByText('Сервер не ответил. Статус бота обновится сам — проверьте его.'),
    ).toBeInTheDocument();
  });

  it('offers no changes while the position closes', async () => {
    open({ status: 'stopped', closeRequested: true }, [position({ orders: [] })]);

    expect(await screen.findByText('Позиция закрывается по рынку…')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Изменить' })).not.toBeInTheDocument();
  });

  it('offers no start to a bot without an account', async () => {
    open({ status: 'stopped', exchangeAccount: null });

    expect(
      await screen.findByText('Привяжите к боту аккаунт биржи, чтобы запустить его.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Запустить' })).not.toBeInTheDocument();
  });

  // The confirmation names the coins: until the instrument is known, nothing with money is offered.
  it('offers the start once the coins of the instrument are known', async () => {
    let answer = () => {};
    open({ status: 'stopped' }, [], {
      'GET /api/market-data/instruments': () =>
        new Promise<Response>((resolve) => {
          answer = () =>
            resolve(
              json([{ category: 'linear', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' }]),
            );
        }),
    });

    expect(await screen.findByRole('button', { name: 'Запустить' })).toBeDisabled();
    answer();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Запустить' })).toBeEnabled());
  });

  // A bot stops for a reason while it still cancels its orders: the reason shows at once.
  it('shows the reason of a bot that stops for it', async () => {
    open({ status: 'stopping', statusReason: 'API key is invalid.' });

    expect(await screen.findByText('API key is invalid.')).toBeInTheDocument();
  });

  // Scenario "Текущая позиция".
  it('shows the current position with its orders', async () => {
    open({ status: 'running' }, [position()]);

    const current = await screen.findByRole('region', { name: 'Текущая позиция' });
    expect(within(current).getByText('открыта')).toBeInTheDocument();
    expect(within(current).getByText('0.5', { selector: 'dd' })).toBeInTheDocument();
    expect(within(current).getByText('исполнен')).toBeInTheDocument();
    expect(within(current).getByText('активен')).toBeInTheDocument();
  });

  // Requirement "Позиции на странице бота": the totals of an order are the truth, its trades only detail that may lag.
  it('shows what an order filled and marks trades that fall short of it', async () => {
    const [bought, sell] = position().orders;
    open({ status: 'running' }, [position({ orders: [{ ...bought, fills: [] }, sell] })]);

    const current = await screen.findByRole('region', { name: 'Текущая позиция' });
    expect(within(current).getByText('0.5 по 100')).toBeInTheDocument();
    expect(within(current).getByText('детализация неполная')).toBeInTheDocument();
  });

  // The orders read as a table: named columns, words for the type, coins for the amounts and fees, and the reason of a
  // refusal as text.
  it('shows the orders of the position with their columns, coins and reasons', async () => {
    open({ status: 'running' }, [
      position({
        orders: [
          {
            number: 1,
            node: 'entry',
            side: 'buy',
            type: 'market',
            price: null,
            quantity: null,
            amount: '100',
            status: 'filled',
            filledQuantity: '0.5',
            averagePrice: '100',
            fee: '0.05',
            rejectReason: null,
            fills: [
              {
                at: '2026-11-03T10:00:00Z',
                price: '100',
                quantity: '0.5',
                fee: '0.05',
                feeAsset: 'quote',
              },
            ],
          },
          {
            number: 2,
            node: 'exit',
            side: 'sell',
            type: 'limit',
            price: '101',
            quantity: '0.5',
            amount: null,
            status: 'rejected',
            filledQuantity: '0',
            averagePrice: null,
            fee: '0',
            rejectReason: 'Insufficient balance.',
            fills: [],
          },
        ],
      }),
    ]);

    const current = await screen.findByRole('region', { name: 'Текущая позиция' });
    expect(
      within(current)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Узел', 'Сторона', 'Тип', 'Цена', 'Объём', 'Исполнено', 'Статус']);
    expect(within(current).getByText('рыночный')).toBeInTheDocument();
    expect(within(current).getByText('лимитный')).toBeInTheDocument();
    expect(within(current).getByText('100 USDT')).toBeInTheDocument();
    expect(within(current).getByText('0.05 USDT')).toBeInTheDocument();
    expect(within(current).getByText(/Insufficient balance\./)).toBeInTheDocument();
  });

  // The server sends the positions the newest first; the page keeps its order.
  it('lists the closed positions in the order of the server', async () => {
    open({ status: 'stopped' }, [
      position({ number: 3, status: 'waiting', quantity: '0', orders: [] }),
      position({
        number: 2,
        status: 'closed',
        closedAt: '2026-11-03T12:00:00Z',
        pnl: '5',
        orders: [],
      }),
      position({
        number: 1,
        status: 'closed',
        closedAt: '2026-11-03T11:00:00Z',
        pnl: '-1',
        orders: [],
      }),
    ]);

    const closed = await screen.findByRole('region', { name: 'Закрытые позиции' });
    expect(
      within(closed)
        .getAllByRole('row')
        .slice(1)
        .map((row) => within(row).getAllByRole('cell').at(0)?.textContent),
    ).toEqual(['2', '1']);
  });
});
