import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
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
        rejectReason: null,
        fills: [],
      },
    ],
    ...overrides,
  };
}

function open(bot: object, positions: unknown[] = [], extra: Record<string, () => Response> = {}) {
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

  // Scenario "Текущая позиция".
  it('shows the current position with its orders', async () => {
    open({ status: 'running' }, [position()]);

    const current = await screen.findByRole('region', { name: 'Текущая позиция' });
    expect(within(current).getByText('открыта')).toBeInTheDocument();
    expect(within(current).getByText('0.5', { selector: 'dd' })).toBeInTheDocument();
    expect(within(current).getByText('исполнен')).toBeInTheDocument();
    expect(within(current).getByText('активен')).toBeInTheDocument();
  });

  it('lists the closed positions the newest first', async () => {
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
