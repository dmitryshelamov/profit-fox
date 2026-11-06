import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

import { createStrategy, reference } from './api';
import { removeOpenIdeas, seedOpenIdea } from './seed';

// Acceptance of the trades by ideas against the real API and a trading Worker on the Bybit stub: the owner enters by an idea on the page of
// its pair, the Worker places the entry with its stop and target, and the stub fills it and fires the price. Nothing reaches the exchange.
// The ideas are seeded: the scanner needs a quarter of an hour to make one. They are on ETHUSDT of the linear market, which the stub quotes
// at 3000 while a test pins its price. Every test leaves no open trade behind, as they share the database.

const stub = 'http://127.0.0.1:5296';
const pair = '/market/linear/ETHUSDT';

interface StubOrder {
  orderLinkId: string;
  parentOrderLinkId: string;
  symbol: string;
  side: string;
  orderType: string;
  orderStatus: string;
  qty: string;
  reduceOnly: boolean;
  takeProfit: string;
  stopLoss: string;
  stopOrderType: string;
}

let accountId = '';

async function stubOrders(request: APIRequestContext): Promise<StubOrder[]> {
  return (await (await request.get(`${stub}/control/orders`)).json()) as StubOrder[];
}

/** The stops and targets the stub made for entries: they have no client id of their own. */
async function stubChildren(request: APIRequestContext): Promise<StubOrder[]> {
  return (await (await request.get(`${stub}/control/children`)).json()) as StubOrder[];
}

/** The orders of trades by ideas on the stub, the entries and the closes: their client ids start with `i`. */
async function tradeOrders(request: APIRequestContext) {
  return (await stubOrders(request)).filter(
    (order) => order.symbol === 'ETHUSDT' && order.orderLinkId.startsWith('i'),
  );
}

async function waitForOrder(request: APIRequestContext, known: number) {
  await expect
    .poll(async () => (await tradeOrders(request)).length, { timeout: 30_000 })
    .toBe(known + 1);

  return (await tradeOrders(request)).at(-1)!;
}

async function settings(request: APIRequestContext, limit = 5) {
  const saved = await request.put('/api/idea-trading/settings', {
    data: {
      accountId,
      risk: 1,
      maxAmount: 200,
      maxOpenTrades: limit,
      maxTotalRisk: 5,
      minRiskReward: 1.5,
    },
  });
  expect(saved.status()).toBe(204);
}

/** Opens the pair, takes «Войти» in the row of the idea of the type and waits for the calculation in the window. */
async function openEntry(page: Page, type: string) {
  await page.goto(pair);
  const row = page
    .getByRole('region', { name: 'Идеи пары' })
    .getByRole('row')
    .filter({ hasText: type });
  await row.getByRole('button', { name: 'Войти' }).click();
  const dialog = page.getByRole('dialog', { name: 'Вход по идее' });
  await expect(dialog.getByText('Цена входа')).toBeVisible();

  return dialog;
}

const tradeRow = (page: Page) =>
  page
    .getByRole('table', { name: 'Сделки по идеям' })
    .getByRole('row')
    .filter({ hasText: 'ETHUSDT' })
    .first();

test.beforeAll(async ({ request }) => {
  test.setTimeout(240_000);
  const uid = String(Date.now());
  const created = await request.post('/api/exchange-accounts', {
    data: { name: `Сделки ${uid}`, apiKey: `e2e-valid-${uid}`, apiSecret: 'e2e-secret' },
  });
  expect(created.status()).toBe(201);
  accountId = ((await created.json()) as { id: string }).id;
  // The pair page reads the market snapshot, which comes after the first closed minute.
  await expect
    .poll(async () => (await request.get('/api/market/linear/ETHUSDT')).status(), {
      timeout: 180_000,
      intervals: [3_000],
    })
    .toBe(200);
});

test.beforeEach(async ({ request }) => {
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3000`);
  await settings(request);
});

test.afterAll(async () => {
  await removeOpenIdeas('ETHUSDT');
});

test.afterEach(async ({ request }) => {
  await request.get(`${stub}/control/price?symbol=ETHUSDT`);
});

test('an entry by an idea places the entry with its stop and target on linear, and the price of the target closes the trade', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'levels',
    side: 'long',
    entry: 3000,
    stop: 2900,
    target: 3200,
    firedMinutesAgo: 5,
  });
  const known = (await tradeOrders(request)).length;

  const dialog = await openEntry(page, 'Уровни');
  await expect(dialog.getByLabel('Аккаунт')).toHaveValue(accountId);
  await expect(dialog.getByText('3000')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Войти' })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Войти' }).click();

  await expect(page).toHaveURL(/\/idea-trades$/);
  const entry = await waitForOrder(request, known);
  // The entry is a market order that carries the stop and the target of the idea.
  expect(entry).toMatchObject({
    side: 'Buy',
    orderType: 'Market',
    takeProfit: '3200',
    stopLoss: '2900',
  });
  await expect(tradeRow(page)).toContainText('Ждёт входа');

  await request.get(`${stub}/control/fill?orderLinkId=${entry.orderLinkId}&price=3000`);
  await expect(tradeRow(page)).toContainText('Открыта', { timeout: 45_000 });
  await expect
    .poll(
      async () =>
        (await stubChildren(request)).filter(
          (order) => order.parentOrderLinkId === entry.orderLinkId,
        ).length,
    )
    .toBe(2);

  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3200`);

  await expect(tradeRow(page)).toContainText('Закрыта по цели', { timeout: 45_000 });
  await expect(tradeRow(page)).toContainText(/\+\d+\.\d{2} USDT · \+\d+\.\d{2} R/);
});

test('a pullback that is not filled by the end of its time is taken off the exchange and the trade is cancelled', async ({
  page,
  request,
}) => {
  test.setTimeout(420_000);
  // It fired 58 minutes ago, and a pullback waits 4 candles of 15 minutes.
  await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'pullback',
    side: 'long',
    entry: 2950,
    stop: 2900,
    target: 3200,
    firedMinutesAgo: 58,
  });
  const known = (await tradeOrders(request)).length;

  const dialog = await openEntry(page, 'Откат');
  await dialog.getByRole('button', { name: 'Войти' }).click();

  await expect(page).toHaveURL(/\/idea-trades$/);
  const entry = await waitForOrder(request, known);
  expect(entry).toMatchObject({ side: 'Buy', orderType: 'Limit' });
  await expect
    .poll(
      async () =>
        (await tradeOrders(request)).find((order) => order.orderLinkId === entry.orderLinkId)
          ?.orderStatus,
      {
        timeout: 300_000,
        intervals: [5_000],
      },
    )
    .toBe('Cancelled');
  await expect(tradeRow(page)).toContainText('Отменена', { timeout: 45_000 });
});

test('«Закрыть по рынку» sells what the trade holds first and takes the stop and the target off after it', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'levels',
    side: 'long',
    entry: 3000,
    stop: 2900,
    target: 3200,
    firedMinutesAgo: 5,
  });
  const known = (await tradeOrders(request)).length;
  const dialog = await openEntry(page, 'Уровни');
  await dialog.getByRole('button', { name: 'Войти' }).click();
  const entry = await waitForOrder(request, known);
  await request.get(`${stub}/control/fill?orderLinkId=${entry.orderLinkId}&price=3000`);
  await expect(tradeRow(page)).toContainText('Открыта', { timeout: 45_000 });

  await tradeRow(page).getByRole('button', { name: 'Закрыть по рынку' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Закрыть по рынку' }).click();

  const close = await waitForOrder(request, known + 1);
  expect(close).toMatchObject({ side: 'Sell', orderType: 'Market', reduceOnly: true });
  const children = async () =>
    (await stubChildren(request))
      .filter((order) => order.parentOrderLinkId === entry.orderLinkId)
      .map((order) => order.orderStatus);
  expect(await children()).not.toContain('Cancelled');
  await request.get(`${stub}/control/fill?orderLinkId=${close.orderLinkId}&price=3010`);

  await expect(tradeRow(page)).toContainText('Закрыта вручную', { timeout: 45_000 });
  await expect.poll(children, { timeout: 45_000 }).toEqual(['Cancelled', 'Cancelled']);
  await expect(tradeRow(page)).toContainText(/\+\d+\.\d{2} USDT/);
});

test('a bot of the opposite direction on the symbol is a reason the entry is refused', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  const uid = String(Date.now());
  const strategyId = await createStrategy(
    request,
    `EMA Short сделки ${uid}`,
    reference('ema-short'),
  );
  const bot = await request.post('/api/bots', {
    data: {
      name: `Шорт сделки ${uid}`,
      strategyId,
      category: 'linear',
      symbol: 'ETHUSDT',
      interval: '60',
      balance: 100,
      exchangeAccountId: accountId,
    },
  });
  expect(bot.status()).toBe(201);
  const botId = ((await bot.json()) as { id: string }).id;
  expect((await request.post(`/api/bots/${botId}/start`)).status()).toBe(204);

  try {
    await seedOpenIdea({
      symbol: 'ETHUSDT',
      type: 'levels',
      side: 'long',
      entry: 3000,
      stop: 2900,
      target: 3200,
      firedMinutesAgo: 5,
    });
    const dialog = await openEntry(page, 'Уровни');

    await expect(dialog.getByText(/противоположная сторона/)).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Войти' })).toBeDisabled();
  } finally {
    await request.post(`/api/bots/${botId}/stop`);
  }
});

test('the limit of open trades is a reason the second entry is refused', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  await settings(request, 1);
  await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'levels',
    side: 'long',
    entry: 3000,
    stop: 2900,
    target: 3200,
    firedMinutesAgo: 5,
  });
  const known = (await tradeOrders(request)).length;
  const first = await openEntry(page, 'Уровни');
  await first.getByRole('button', { name: 'Войти' }).click();
  await waitForOrder(request, known);
  await expect(tradeRow(page)).toContainText('Ждёт входа');

  const second = await openEntry(page, 'Уровни');

  await expect(second.getByText('Уже открыто столько сделок, сколько разрешено')).toBeVisible();
  await expect(second.getByRole('button', { name: 'Войти' })).toBeDisabled();

  // The first trade is taken back, so the database holds no open trade for the next test.
  await page.goto('/idea-trades');
  await tradeRow(page).getByRole('button', { name: 'Отменить' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Отменить сделку' }).click();
  await expect(tradeRow(page)).toContainText('Отменена', { timeout: 45_000 });
});
