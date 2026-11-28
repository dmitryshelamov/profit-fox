import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Account } from '@/api/accounts';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea, ideaPage, scannerState } from '@/test/ideas';
import { pair } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { preview, riskSettings, riskState, trade } from '@/test/trades';

const main: Account = {
  id: 'a1',
  name: 'Основной',
  uid: 1,
  apiKey: 'k',
  marginMode: 'ISOLATED_MARGIN',
  createdAt: '2026-11-27T11:20:00Z',
};

const enabled = {
  name: 'breakout-long',
  description: null,
  version: 7,
  error: null,
  signals: [],
  exits: [],
  kind: 'idea',
  ideas: { enabled: true, timeframes: ['1h'] },
};

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/strategies': () => json([enabled]),
    'GET /api/ideas/state': () => json(scannerState()),
    'GET /api/ideas': ideaPage([idea()]),
    'GET /api/ideas/summary': () =>
      json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
    'GET /api/accounts': () => json([main]),
    'GET /api/risk/state': () => json(riskState()),
    'GET /api/risk/settings': () => json(riskSettings()),
    'POST /api/trades/preview': () => json(preview()),
    ...extra,
  });
}

const row = () => screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
const enter = async () => within(await row()).findByRole('button', { name: /^Войти/ });

async function openDialog() {
  const button = await enter();
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
  return within(await screen.findByRole('dialog', { name: 'Вход в сделку' }));
}

// Change take-idea-trades, task 9.5: spec idea-trades «Кнопка „Войти“ у идеи».
describe('the column «Сделка» of the ideas', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Можно войти».
  it('has «Войти» that opens the entry dialog and not the page of the pair', async () => {
    routes();
    const app = renderApp('/ideas');

    await openDialog();

    expect(app.history.location.pathname).toBe('/ideas');
    await row();
    expect(screen.getByRole('columnheader', { name: /Сделка/ })).toBeInTheDocument();
  });

  // Scenario «Уже в сделке».
  it('shows «в сделке» in place of the button when the idea has a trade', async () => {
    routes({ 'GET /api/ideas': ideaPage([idea({ trade: { id: 'trade-sol', state: 'open' } })]) });
    renderApp('/ideas');

    const cell = await row();
    expect(await within(cell).findByText('в сделке')).toBeInTheDocument();
    expect(within(cell).queryByRole('button')).toBeNull();
  });

  it('names the state of a trade that is not holding the pair', async () => {
    routes({ 'GET /api/ideas': ideaPage([idea({ trade: { id: 't', state: 'error' } })]) });
    renderApp('/ideas');

    expect(await within(await row()).findByText('ошибка')).toBeInTheDocument();
  });

  // Scenario «Аккаунтов нет».
  it('disables «Войти» with the reason when there is no account', async () => {
    routes({ 'GET /api/risk/state': () => json(riskState({ accounts: 0 })) });
    renderApp('/ideas');

    const button = await enter();
    await waitFor(() => expect(button).toHaveAttribute('title', 'добавьте аккаунт в «Аккаунтах»'));
    expect(button).toBeDisabled();
  });

  // Scenario «Стоп-кран».
  it('disables «Войти» with the halt as the reason', async () => {
    routes({
      'GET /api/risk/state': () =>
        json(
          riskState({
            halt: { haltedAt: Date.now(), loss: 52.1, limit: 50, notClosed: [], liftedAt: null },
          }),
        ),
    });
    renderApp('/ideas');

    const button = await enter();
    await waitFor(() => expect(button).toHaveAttribute('title', 'вход запрещён стоп-краном'));
    expect(button).toBeDisabled();
  });

  // Scenario «Свежий стенд».
  it('disables «Войти» when the limits are not set', async () => {
    routes({ 'GET /api/risk/state': () => json(riskState({ limitsSet: false })) });
    renderApp('/ideas');

    const button = await enter();
    await waitFor(() =>
      expect(button).toHaveAttribute('title', 'задайте пределы в разделе «Риск»'),
    );
    expect(button).toBeDisabled();
  });

  it('disables «Войти» when no trading Worker works', async () => {
    routes({ 'GET /api/risk/state': () => json(riskState({ traderAlive: false, trader: null })) });
    renderApp('/ideas');

    const button = await enter();
    await waitFor(() => expect(button).toBeDisabled());
    expect(button.getAttribute('title')).toMatch(/нет торгового Worker/);
  });

  it('has no button on a closed idea of a pair', async () => {
    routes({
      'GET /api/market/pairs/SOLUSDT': () => json(pair('SOLUSDT')),
      'GET /api/market/pairs/SOLUSDT/ideas': () =>
        json({
          items: [idea({ state: 'closed', exitReason: 'stop', result: -0.02 })],
          total: 1,
          page: 1,
          pageSize: 50,
          filterGone: false,
        }),
    });
    renderApp('/market/SOLUSDT?timeframe=1h');

    await screen.findByRole('row', { name: /breakout-long 1h закрыта/ });
    expect(screen.queryByRole('button', { name: /^Войти/ })).toBeNull();
  });

  it('is in the table of the ideas of a pair too', async () => {
    routes({
      'GET /api/market/pairs/SOLUSDT': () => json(pair('SOLUSDT')),
      'GET /api/market/pairs/SOLUSDT/ideas': () =>
        json({ items: [idea()], total: 1, page: 1, pageSize: 50, filterGone: false }),
    });
    renderApp('/market/SOLUSDT?timeframe=1h');

    const button = await screen.findByRole('button', { name: 'Войти SOLUSDT' });
    await waitFor(() => expect(button).toBeEnabled());
    await userEvent.click(button);
    expect(await screen.findByRole('dialog', { name: 'Вход в сделку' })).toBeInTheDocument();
  });
});

// Task 9.6: spec idea-trades «Диалог входа», «Расчёт перед входом».
describe('the entry dialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the idea, its levels against the price now, the count and the checks', async () => {
    routes();
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByText('0.4 SOL ≈ 94.96 USDT')).toBeInTheDocument();
    expect(dialog.getByText('SOLUSDT')).toBeInTheDocument();
    expect(dialog.getByText(/breakout-long v7 · 1h/)).toBeInTheDocument();
    const levels = within(dialog.getByRole('table', { name: 'Уровни входа' }));
    expect(levels.getByText('237.40')).toBeInTheDocument();
    expect(levels.getByText('+0.55 % к идее')).toBeInTheDocument();
    expect(levels.getByText('-2.54 %')).toBeInTheDocument();
    expect(levels.getByText('+3.43 %')).toBeInTheDocument();
    expect(dialog.getByText('31.65 USDT')).toBeInTheDocument();
    expect(dialog.getByText(/−2.51 USDT/)).toBeInTheDocument();
    expect(dialog.getByText(/\+3.16 USDT/)).toBeInTheDocument();
    expect(dialog.getByText('стоп идеи 231.38 (предел 3 % — 230.28)')).toBeInTheDocument();
    expect(dialog.getByText('28.11 18:00 по рынку')).toBeInTheDocument();
    expect(dialog.getByText('свободно 1 250.40 USDT')).toBeInTheDocument();
    expect(dialog.getByLabelText('Плечо')).toHaveValue('3×');
    expect(dialog.getByLabelText('Плечо')).toHaveAttribute('readonly');
    expect(dialog.getByLabelText('Аккаунт')).toHaveValue('a1');
    expect(dialog.getByLabelText('Сумма сделки')).toHaveValue('100');
  });

  it('shows every check, the margin mode among them in the left column', async () => {
    routes();
    renderApp('/ideas');
    const dialog = await openDialog();

    const left = within(await dialog.findByRole('list', { name: 'Проверки слева' }));
    expect(left.getAllByRole('listitem').map((item) => item.getAttribute('data-code'))).toEqual([
      'amount',
      'total',
      'position',
      'margin',
    ]);
    expect(left.getByText(/Аккаунт в режиме изолированной маржи/)).toBeInTheDocument();
    const right = within(dialog.getByRole('list', { name: 'Проверки справа' }));
    expect(right.getAllByRole('listitem')).toHaveLength(3);
  });

  it('puts a failed check of any code in the list, red with its reason', async () => {
    const failed = preview({
      passed: false,
      checks: [
        ...preview().checks.slice(0, 3),
        { code: 'margin', passed: false, text: 'Аккаунт в кросс-марже: нужна изолированная' },
        { code: 'halt', passed: false, text: 'Вход запрещён стоп-краном' },
      ],
    });
    routes({ 'POST /api/trades/preview': () => json(failed) });
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByText(/Аккаунт в кросс-марже/)).toBeInTheDocument();
    expect(dialog.getByText(/Вход запрещён стоп-краном/).closest('li')).toHaveAttribute(
      'data-passed',
      'false',
    );
  });

  // Scenario «Кнопка лонга».
  it('names «Купить на 100 USDT» on the button of a long idea once every check passed', async () => {
    routes();
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByRole('button', { name: 'Купить на 100 USDT' })).toBeEnabled();
  });

  // Scenario «Кнопка шорта».
  it('names «Продать на 100 USDT» on the button of a short idea', async () => {
    routes({
      'GET /api/ideas': ideaPage([idea({ side: 'short' })]),
      'POST /api/trades/preview': () => json(preview({ side: 'short' })),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByRole('button', { name: 'Продать на 100 USDT' })).toBeEnabled();
  });

  // Scenario «Проверка не прошла».
  it('shows the check that did not pass red and keeps the button disabled', async () => {
    routes({
      'POST /api/trades/preview': () =>
        json(
          preview({
            passed: false,
            checks: [
              { code: 'amount', passed: false, text: 'Сумма 300 больше предела 200 USDT' },
              ...preview().checks.slice(1),
            ],
          }),
        ),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    const check = (await dialog.findByText(/Сумма 300 больше предела 200 USDT/)).closest('li');
    expect(check).toHaveClass('text-err');
    expect(dialog.getByRole('button', { name: 'Купить на 100 USDT' })).toBeDisabled();
  });

  // Scenario «Идея без стопа».
  it('says the idea has no stop and shows the stop at the limit', async () => {
    routes({
      'GET /api/ideas': ideaPage([idea({ stop: null, stopFraction: null, side: 'short' })]),
      'POST /api/trades/preview': () =>
        json(
          preview({
            side: 'short',
            price: 1.4126,
            ideaStop: null,
            ideaTarget: null,
            target: null,
            stop: 1.455,
            stopSource: 'limit',
            limitStop: 1.455,
            profitAtTarget: null,
          }),
        ),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(
      await dialog.findByText('у идеи стопа нет · по пределу 3 % — 1.4550'),
    ).toBeInTheDocument();
  });

  it('reads the count again with the amount typed and the account chosen', async () => {
    const requests = routes({
      'GET /api/accounts': () => json([main, { ...main, id: 'a2', name: 'Суб-счёт идей' }]),
    });
    renderApp('/ideas');
    const dialog = await openDialog();
    await dialog.findByRole('button', { name: 'Купить на 100 USDT' });

    const amount = dialog.getByLabelText('Сумма сделки');
    await userEvent.clear(amount);
    await userEvent.type(amount, '150');
    await userEvent.selectOptions(dialog.getByLabelText('Аккаунт'), 'a2');

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/trades/preview')).toContainEqual({
        ideaId: 'idea-sol',
        accountId: 'a2',
        amount: 150,
      }),
    );
    expect(dialog.getByRole('button', { name: 'Купить на 150 USDT' })).toBeInTheDocument();
  });

  // Scenario «Вход отклонён сервером».
  it('stays open and shows the reasons when the server refuses the entry', async () => {
    routes({
      'POST /api/trades': () =>
        json(
          {
            status: 422,
            checks: [
              {
                code: 'levels',
                passed: false,
                text: 'Вход отклонён: цена 231.10 уже ниже стопа идеи 231.38',
              },
            ],
          },
          422,
        ),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    await userEvent.click(await dialog.findByRole('button', { name: 'Купить на 100 USDT' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'Вход отклонён: цена 231.10 уже ниже стопа идеи 231.38',
    );
    expect(screen.getByRole('dialog', { name: 'Вход в сделку' })).toBeInTheDocument();
  });

  it('enters, closes and asks the ideas and the trades again when the server takes the entry', async () => {
    const requests = routes({
      'POST /api/trades': () => json(trade({ state: 'pending' }), 201),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    await userEvent.click(await dialog.findByRole('button', { name: 'Купить на 100 USDT' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    // Review R4: the amount the dialog shows goes, not «the default of the settings now».
    expect(await bodiesOf(requests, 'POST', '/api/trades')).toEqual([
      { ideaId: 'idea-sol', accountId: 'a1', amount: 100 },
    ]);
  });

  // Review R5: a target nearer than the fees is a loss, and its share has the minus too.
  it('writes the sign of the profit at the target from the number', async () => {
    routes({ 'POST /api/trades/preview': () => json(preview({ profitAtTarget: -0.05 })) });
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByText(/[−-]0\.05 USDT · [−-]\s*0\.1 %/)).toBeInTheDocument();
  });

  it('says so when Bybit gave no count', async () => {
    routes({
      'POST /api/trades/preview': () => json({ status: 503, detail: 'Bybit не отвечает' }, 503),
    });
    renderApp('/ideas');
    const dialog = await openDialog();

    expect(await dialog.findByText('Расчёт не получен: Bybit не отвечает')).toBeInTheDocument();
  });
});
