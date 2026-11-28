import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { riskSettings, riskState, trade, tradePage } from '@/test/trades';

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/risk/state': () => json(riskState()),
    'GET /api/risk/settings': () => json(riskSettings()),
    'GET /api/trades': tradePage([trade()], { closed: 37 }),
    ...extra,
  });
}

const cells = (row: HTMLElement) =>
  within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent);

// Change take-idea-trades, tasks 9.2 and 9.7: spec idea-trades «Раздел „Сделки“».
describe('the section «Сделки»', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario «Открытая сделка».
  it('shows an open trade with its pair, account, idea, side, amount, levels, time left, result now and state', async () => {
    routes();
    renderApp('/trades');

    const row = await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    expect(cells(row)).toEqual([
      'SOLUSDT',
      'Основной',
      'breakout-longv7 · 1h',
      'лонг',
      '100 · 3×',
      '237.42',
      '231.38',
      '245.54',
      '2 ч 12 мин',
      '+1.18 USDT+1.2 %',
      'Открыта',
      'Закрыть',
    ]);
    expect(within(row).getByText('+1.18 USDT').parentElement).toHaveClass('text-ok');
    expect(screen.getByRole('tab', { name: /Открытые/ })).toHaveTextContent('1');
    expect(screen.getByRole('tab', { name: /Закрытые/ })).toHaveTextContent('37');
  });

  it('shows a losing result red and the stop at the limit with its percent', async () => {
    routes({
      'GET /api/trades': tradePage([
        trade({
          symbol: 'XRPUSDT',
          side: 'short',
          clickPrice: 1.4126,
          stop: 1.455,
          stopSource: 'limit',
          unrealised: -0.52,
        }),
      ]),
    });
    renderApp('/trades');

    const row = await screen.findByRole('row', { name: /XRPUSDT/ });
    expect(within(row).getByText('-0.52 USDT').parentElement).toHaveClass('text-err');
    expect(within(row).getByText('-0.5 %')).toBeInTheDocument();
    expect(within(row).getByText('по пределу 3 %')).toBeInTheDocument();
    expect(within(row).getByText('шорт')).toBeInTheDocument();
  });

  it('follows the open trades every 5 seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let rows = [trade()];
    const requests = routes({ 'GET /api/trades': () => tradePage(rows)(new Request('http://x')) });
    renderApp('/trades');
    await screen.findByRole('row', { name: /SOLUSDT/ });

    rows = [trade({ unrealised: 2.5 })];
    await act(() => vi.advanceTimersByTimeAsync(5100));
    expect(await screen.findByText('+2.50 USDT')).toBeInTheDocument();
    expect(
      requests.filter((request) => new URL(request.url).pathname === '/api/trades').length,
    ).toBeGreaterThanOrEqual(2);
  });

  // Scenarios «Отменённая сделка» and the columns of the closed ones.
  it('lists a cancelled trade in «Закрытые» with its reason and no outcome', async () => {
    routes({
      'GET /api/trades': tradePage(
        [
          trade({
            id: 'cancelled',
            symbol: 'LINKUSDT',
            state: 'cancelled',
            reason: 'вход не ушёл вовремя',
            entryPrice: null,
            unrealised: null,
            openedAt: null,
            closedAt: Date.UTC(2026, 10, 26, 18, 30),
            ideaResult: -0.006,
          }),
          trade({
            id: 'won',
            symbol: 'AVAXUSDT',
            state: 'closed',
            closeReason: 'target',
            fees: 0.11,
            result: 2.74,
            resultShare: 0.027,
            ideaResult: 0.0289,
            openedAt: Date.UTC(2026, 10, 28, 8, 30),
            closedAt: Date.UTC(2026, 10, 28, 9, 2),
          }),
        ],
        { open: 1, closed: 2 },
      ),
    });
    renderApp('/trades?tab=closed');

    const cancelled = await screen.findByRole('row', { name: /LINKUSDT/ });
    expect(within(cancelled).getByText('отменена · вход не ушёл вовремя')).toBeInTheDocument();
    expect(cells(cancelled).slice(-3)).toEqual(['—', '—', '-0.60 %']);
    const won = screen.getByRole('row', { name: /AVAXUSDT/ });
    expect(cells(won)).toEqual(
      expect.arrayContaining([
        '28.11 11:30',
        '28.11 12:02',
        'по цели',
        '0.11',
        '+2.74',
        '+2.7 %',
        '+2.89 %',
      ]),
    );
    expect(screen.getByRole('columnheader', { name: /Вход, UTC\+3/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Закрытые/ })).toHaveAttribute('aria-selected', 'true');
  });

  it('asks the closed trades when the tab «Закрытые» is chosen', async () => {
    const requests = routes();
    renderApp('/trades');
    await screen.findByRole('row', { name: /SOLUSDT/ });

    await userEvent.click(screen.getByRole('tab', { name: /Закрытые/ }));

    await waitFor(() =>
      expect(
        requests.some((request) => new URL(request.url).searchParams.get('state') === 'closed'),
      ).toBe(true),
    );
  });

  // Scenario «Сделок нет».
  it('says a trade opens with «Войти» at an idea and leads to «Идеи» when there were no trades', async () => {
    routes({ 'GET /api/trades': tradePage([], { open: 0, closed: 0 }) });
    const { history } = renderApp('/trades');

    const empty = await screen.findByRole('region', { name: 'Сделок нет' });
    expect(empty).toHaveTextContent('Сделка открывается кнопкой «Войти» у идеи');
    await userEvent.click(within(empty).getByRole('link', { name: 'Идеи' }));
    await waitFor(() => expect(history.location.pathname).toBe('/ideas'));
  });

  // Scenario «Клик по сделке».
  it('opens the page of the pair on the timeframe of the idea with the idea chosen on a click on the row', async () => {
    routes();
    const app = renderApp('/trades');

    await userEvent.click(await screen.findByRole('row', { name: /SOLUSDT/ }));

    await waitFor(() => expect(app.history.location.pathname).toBe('/market/SOLUSDT'));
    expect(app.history.location.search).toContain('timeframe=1h');
    expect(app.history.location.search).toContain('idea=idea-sol');
  });

  // Scenario «Итог идеи рядом».
  it('shows the result of the idea beside the outcome of a closed trade', async () => {
    routes({
      'GET /api/trades': tradePage(
        [
          trade({
            state: 'closed',
            closeReason: 'stop',
            result: -2.33,
            resultShare: -0.023,
            ideaResult: -0.0211,
          }),
        ],
        { open: 0, closed: 1 },
      ),
    });
    renderApp('/trades?tab=closed');

    const row = await screen.findByRole('row', { name: /SOLUSDT/ });
    expect(within(row).getByText('-2.3 %')).toHaveClass('text-err');
    expect(within(row).getByText('-2.11 %')).toBeInTheDocument();
    expect(within(row).getByText('по стопу')).toBeInTheDocument();
  });
});

// Task 9.4: spec trade-risk «Риск в „Сделках“», «Стоп-кран на экране»; spec idea-trades «Связь с биржей на экране».
describe('the risk in «Сделки»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Полоса риска».
  it('shows the loss of the day, the open trades and the sum beside their limits and a link to «Риск»', async () => {
    routes();
    renderApp('/trades');

    const strip = await screen.findByRole('region', { name: 'Состояние риска' });
    expect(strip).toHaveTextContent('−4.20 из −50.00 USDT');
    expect(strip).toHaveTextContent('2 из 5');
    expect(strip).toHaveTextContent('300 из 1 000 USDT');
    expect(screen.getByRole('link', { name: 'Риск →' })).toHaveAttribute('href', '/risk');
  });

  // Scenario «Пределов нет».
  it('asks to set the limits instead of the strip when they are not set', async () => {
    routes({ 'GET /api/risk/state': () => json(riskState({ limitsSet: false })) });
    renderApp('/trades');

    const note = await screen.findByRole('status');
    expect(note).toHaveTextContent('Задайте пределы в разделе Риск, чтобы входить в идеи');
    expect(within(note).getByRole('link', { name: 'Риск' })).toHaveAttribute('href', '/risk');
    expect(screen.queryByRole('region', { name: 'Состояние риска' })).toBeNull();
  });

  // Scenario «Баннер в „Сделках“».
  it('shows the halt with its moment, loss and limit, leading to «Риск» and with no button to lift it', async () => {
    routes({
      'GET /api/risk/state': () =>
        json(
          riskState({
            halt: {
              haltedAt: Date.UTC(2026, 10, 28, 12, 42),
              loss: 52.1,
              limit: 50,
              notClosed: ['DOGEUSDT'],
              liftedAt: null,
            },
          }),
        ),
    });
    renderApp('/trades');

    const banner = await screen.findByRole('region', { name: 'Баннер стоп-крана' });
    expect(banner).toHaveTextContent('28.11 15:42');
    expect(banner).toHaveTextContent('52.10');
    expect(banner).toHaveTextContent('50.00');
    expect(banner).toHaveTextContent('DOGEUSDT');
    expect(within(banner).getByRole('link', { name: 'Открыть «Риск» →' })).toHaveAttribute(
      'href',
      '/risk',
    );
    expect(within(banner).queryByRole('button', { name: 'Снять запрет' })).toBeNull();
  });

  // Scenario «Аккаунт на опросе».
  it('names the account polled instead of streamed and since when', async () => {
    routes({
      'GET /api/risk/state': () =>
        json(
          riskState({
            polled: [{ id: 'a1', name: 'Основной', since: Date.UTC(2026, 10, 28, 12, 42) }],
          }),
        ),
    });
    renderApp('/trades');

    expect(
      await screen.findByText('Основной ведётся опросом с 15:42: поток Bybit оборван'),
    ).toBeInTheDocument();
  });

  // Scenario «Все на потоке».
  it('has no line about the link when every stream is alive', async () => {
    routes();
    renderApp('/trades');

    await screen.findByRole('region', { name: 'Состояние риска' });
    expect(screen.queryByText(/ведётся опросом/)).toBeNull();
  });
});

// Task 9.7: spec idea-trades «Закрытие вручную», «Ошибку разбирает владелец».
describe('the actions of a row in «Сделки»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Закрыть по рынку».
  it('asks before closing by the market, naming the pair, the side, the amount and the result now', async () => {
    const requests = routes({
      'POST /api/trades/trade-sol/close': () => json(trade({ state: 'closing' })),
    });
    renderApp('/trades');

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть SOLUSDT' }));

    const dialog = await screen.findByRole('dialog', { name: 'Закрыть SOLUSDT по рынку?' });
    expect(dialog).toHaveTextContent('Лонг на 100 USDT, сейчас +1.18 USDT');
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Закрыть по рынку' }));

    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith('/api/trades/trade-sol/close'))).toBe(
        true,
      ),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('closes nothing when the owner does not confirm', async () => {
    const requests = routes();
    renderApp('/trades');

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть SOLUSDT' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Не закрывать' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  it('shows the refusal of the server after a close', async () => {
    routes({
      'POST /api/trades/trade-sol/close': () =>
        json({ status: 409, detail: 'сделка уже закрывается' }, 409),
    });
    renderApp('/trades');

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть SOLUSDT' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));

    expect(await screen.findByText('SOLUSDT: сделка уже закрывается')).toHaveAttribute(
      'role',
      'alert',
    );
  });

  // Scenario «Сделка ещё ждёт входа».
  it('has no close button on a trade that waits for its entry', async () => {
    routes({
      'GET /api/trades': tradePage([
        trade({ state: 'pending', entryPrice: null, unrealised: null }),
      ]),
    });
    renderApp('/trades');

    const row = await screen.findByRole('row', { name: /SOLUSDT/ });
    expect(within(row).getByText('Ждёт входа')).toBeInTheDocument();
    expect(within(row).queryByRole('button')).toBeNull();
  });

  // Review R1: Bybit no longer takes the key — «Разобрана» asks «Позиция на Bybit закрыта?» and resolves without the check;
  // R6: the button is off while its request goes.
  it('asks whether the position on Bybit is closed when the server cannot check it, and resolves without the check', async () => {
    let answer: (() => void) | null = null;
    const requests = routes({
      'GET /api/trades': tradePage([
        trade({
          id: 'doge',
          symbol: 'DOGEUSDT',
          state: 'error',
          reason: 'позиция на бирже больше сделки',
        }),
      ]),
      'POST /api/trades/doge/resolve': (request) =>
        new URL(request.url).searchParams.get('unchecked') === 'true'
          ? json(trade({ state: 'closed' }))
          : new Promise<Response>((resolve) => {
              answer = () =>
                resolve(
                  json(
                    {
                      status: 409,
                      detail:
                        'Позицию не проверить: Bybit не принимает ключ аккаунта (API key is invalid.).',
                      canSkipCheck: true,
                    },
                    409,
                  ),
                );
            }),
    });
    renderApp('/trades');

    const row = await screen.findByRole('row', { name: /DOGEUSDT/ });
    await userEvent.click(within(row).getByRole('button', { name: 'Разобрана DOGEUSDT' }));
    await waitFor(() =>
      expect(within(row).getByRole('button', { name: 'Разобрана DOGEUSDT' })).toBeDisabled(),
    );
    act(() => answer!());

    const dialog = within(await screen.findByRole('dialog', { name: 'Позиция на Bybit закрыта?' }));
    expect(dialog.getByText(/Bybit не принимает ключ аккаунта/)).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Позиция закрыта — разобрать' }));

    await waitFor(() =>
      expect(
        requests.filter((request) => request.url.includes('/doge/resolve?unchecked=true')),
      ).toHaveLength(1),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  // Scenarios «Позиция ещё есть» and «Позиции нет».
  it('shows the error of a trade and marks it resolved, or shows why the server refused', async () => {
    let refused = true;
    const requests = routes({
      'GET /api/trades': tradePage([
        trade({
          id: 'doge',
          symbol: 'DOGEUSDT',
          state: 'error',
          reason: 'позиция на бирже больше сделки',
          unrealised: -5.9,
        }),
      ]),
      'POST /api/trades/doge/resolve': () =>
        refused
          ? json({ status: 409, detail: 'сначала закройте позицию на Bybit' }, 409)
          : json(trade({ state: 'closed' })),
    });
    renderApp('/trades');

    expect(
      await screen.findByText('Ошибка · DOGEUSDT: позиция на бирже больше сделки'),
    ).toBeInTheDocument();
    const row = screen.getByRole('row', { name: /DOGEUSDT/ });
    expect(within(row).queryByRole('button', { name: /Закрыть/ })).toBeNull();
    await userEvent.click(within(row).getByRole('button', { name: 'Разобрана DOGEUSDT' }));
    expect(
      await screen.findByText('DOGEUSDT: сначала закройте позицию на Bybit'),
    ).toBeInTheDocument();

    refused = false;
    await userEvent.click(within(row).getByRole('button', { name: 'Разобрана DOGEUSDT' }));
    await waitFor(() =>
      expect(requests.filter((request) => request.url.endsWith('/doge/resolve'))).toHaveLength(2),
    );
  });
});
