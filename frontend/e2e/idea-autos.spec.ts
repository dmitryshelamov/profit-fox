import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

import {
  removeAutos,
  removeOpenIdeas,
  seedAutoEntries,
  seedClosedTrade,
  seedOpenIdea,
} from './seed';

// Acceptance of the autos with money against the real API and a trading Worker on the Bybit stub. The scanner needs a quarter of an hour
// to make an idea, so the idea of ETHUSDT is seeded with the requests of the autos exactly as the scanner writes them in the minute of
// the idea; the Worker enters by them. The stub quotes ETHUSDT at 3000 while the test pins it. Nothing reaches the exchange.

const stub = 'http://127.0.0.1:5296';
const upper = 'E2E верхний';
const lower = 'E2E нижний';

interface StubOrder {
  orderLinkId: string;
  parentOrderLinkId: string;
  symbol: string;
  side: string;
  orderType: string;
  takeProfit: string;
  stopLoss: string;
}

interface AutoItem {
  auto: { id: string; name: string; position: number };
}

let accountId = '';

/** The orders of trades by ideas on the stub for ETHUSDT: their client ids start with `i`. */
async function tradeOrders(request: APIRequestContext): Promise<StubOrder[]> {
  const orders = (await (await request.get(`${stub}/control/orders`)).json()) as StubOrder[];

  return orders.filter((order) => order.symbol === 'ETHUSDT' && order.orderLinkId.startsWith('i'));
}

async function settings(request: APIRequestContext, dailyLossLimit: number | null = null) {
  const saved = await request.put('/api/idea-trading/settings', {
    data: {
      accountId,
      amount: 30,
      maxAmount: 200,
      maxOpenTrades: 10,
      maxTotalRisk: 100,
      minRiskReward: 1.5,
      dailyLossLimit,
      maxSameDirection: null,
    },
  });
  expect(saved.status()).toBe(204);
}

/** Creates an auto «С деньгами» by the form: the account of the test, 30 USDT, «Импульс» «Цель 2R» on 15 minutes. */
async function createByForm(page: Page, name: string) {
  await page.goto('/idea-trades/autos/new');
  await page.getByLabel('Название').fill(name);
  await page.getByLabel('Аккаунт').selectOption(accountId);
  await page.getByLabel('Сумма на сделку, USDT').fill('30');
  await page
    .getByRole('group', { name: 'Сетапы' })
    .getByRole('button', { name: 'Импульс', exact: true })
    .click();
  await page
    .getByRole('group', { name: 'Типы идей' })
    .getByRole('button', { name: 'Цель 2R', exact: true })
    .click();
  await page
    .getByRole('group', { name: 'ТФ' })
    .getByRole('button', { name: '15 мин', exact: true })
    .click();
  await page
    .getByRole('radiogroup', { name: 'Режим' })
    .getByRole('radio', { name: 'С деньгами' })
    .click();
  await page.getByRole('button', { name: 'Сохранить' }).click();
  await expect(page).toHaveURL(/\/idea-trades\/autos$/);
  await expect(
    page.getByRole('table', { name: 'Автоматы' }).locator(`[data-auto="${name}"]`),
  ).toBeVisible();
}

async function autos(request: APIRequestContext): Promise<AutoItem[]> {
  return (await (await request.get('/api/idea-autos')).json()) as AutoItem[];
}

const refusalRow = (page: Page, name: string) =>
  page
    .getByRole('table', { name: 'Отказы автоматов' })
    .getByRole('row')
    .filter({ hasText: name })
    .first();

test.beforeAll(async ({ request }) => {
  await removeAutos();
  const uid = String(Date.now());
  const created = await request.post('/api/exchange-accounts', {
    data: { name: `Автоматы ${uid}`, apiKey: `e2e-valid-${uid}`, apiSecret: 'e2e-secret' },
  });
  expect(created.status()).toBe(201);
  accountId = ((await created.json()) as { id: string }).id;
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3000`);
  await settings(request);
});

test.afterAll(async ({ request }) => {
  await request.get(`${stub}/control/price?symbol=ETHUSDT`);
  await removeAutos();
  await removeOpenIdeas('ETHUSDT');
  await settings(request);
});

test('two autos with money made by the form take an idea in the order of the list: the upper enters, the lower finds the pair taken, the daily stop stops them', async ({
  page,
  request,
}) => {
  test.setTimeout(300_000);
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));

  // The lower one is made first, so dragging the upper to the top is what puts it first.
  await createByForm(page, lower);
  await createByForm(page, upper);
  const table = page.getByRole('table', { name: 'Автоматы' });
  await table
    .getByRole('button', { name: `Перетащить «${upper}»` })
    .dragTo(table.locator(`[data-auto="${lower}"]`));
  await expect
    .poll(async () => (await autos(request)).map((item) => item.auto.name))
    .toEqual([upper, lower]);
  await page.reload();
  await expect(table.locator('[data-auto]').first()).toHaveAttribute('data-auto', upper);

  // The idea is born and both autos ask for it in the same minute; the Worker takes the requests by the place of the auto.
  const [first, second] = await autos(request);
  const known = (await tradeOrders(request)).length;
  const idea = await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'target_2r',
    side: 'long',
    entry: 3000,
    stop: 2950,
    target: 3100,
    firedMinutesAgo: 1,
  });
  await seedAutoEntries([
    {
      ideaId: idea,
      autoId: second!.auto.id,
      accountId,
      amount: 30,
      minRiskReward: 1.5,
      rank: second!.auto.position,
    },
    {
      ideaId: idea,
      autoId: first!.auto.id,
      accountId,
      amount: 30,
      minRiskReward: 1.5,
      rank: first!.auto.position,
    },
  ]);

  // On the stub only the entry of the upper auto, with the stop and the target of the idea.
  await expect
    .poll(async () => (await tradeOrders(request)).length, { timeout: 60_000 })
    .toBe(known + 1);
  const entry = (await tradeOrders(request)).at(-1)!;
  expect(entry).toMatchObject({
    side: 'Buy',
    orderType: 'Market',
    takeProfit: '3100',
    stopLoss: '2950',
  });

  await page.goto('/idea-trades');
  await expect(refusalRow(page, lower)).toContainText('Уже есть сделка по паре', {
    timeout: 30_000,
  });
  await expect(refusalRow(page, lower)).toContainText(`её взял «${upper}» — он выше в списке`);
  // The refusal of the lower auto is the only one: the upper auto entered, its name is in the detail only.
  await expect(
    page.getByRole('table', { name: 'Отказы автоматов' }).locator('tbody tr'),
  ).toHaveCount(1);

  // The stub fills the entry and the price reaches the target: the trade of the auto closes at the target with its name.
  await request.get(`${stub}/control/fill?orderLinkId=${entry.orderLinkId}&price=3000`);
  await expect
    .poll(async () => {
      const children = (await (
        await request.get(`${stub}/control/children`)
      ).json()) as StubOrder[];

      return children.filter((order) => order.parentOrderLinkId === entry.orderLinkId).length;
    })
    .toBe(2);
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3100`);
  await page
    .getByRole('radiogroup', { name: 'Кто взял' })
    .getByRole('radio', { name: 'Авто' })
    .click();
  const closed = page
    .getByRole('table', { name: 'Закрытые сделки' })
    .getByRole('row')
    .filter({ hasText: 'ETHUSDT' })
    .first();
  await expect(closed).toContainText('Закрыта по цели', { timeout: 60_000 });
  await expect(closed).toContainText(upper);
  await expect(closed).toHaveAttribute('data-origin', 'auto');
  await page
    .getByRole('radiogroup', { name: 'Кто взял' })
    .getByRole('radio', { name: 'Все' })
    .click();

  // A real trade closed at a loss of 35 today with a daily stop of 30: the next request of the upper auto is refused, and the banner shows.
  await settings(request, 30);
  await seedClosedTrade(accountId, 35);
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3000`);
  const next = await seedOpenIdea({
    symbol: 'ETHUSDT',
    type: 'target_2r',
    side: 'long',
    entry: 3000,
    stop: 2950,
    target: 3100,
    firedMinutesAgo: 1,
  });
  await seedAutoEntries([
    {
      ideaId: next,
      autoId: first!.auto.id,
      accountId,
      amount: 30,
      minRiskReward: 1.5,
      rank: first!.auto.position,
    },
  ]);

  await page.goto('/idea-trades');
  await expect(refusalRow(page, upper)).toContainText('Дневной стоп', { timeout: 30_000 });
  await expect(page.getByRole('status', { name: 'Дневной стоп' })).toContainText(
    'Дневной стоп сработал — автоматы не входят до 00:00 UTC',
  );
  expect((await tradeOrders(request)).length).toBe(known + 1);
  expect(errors).toEqual([]);
});
