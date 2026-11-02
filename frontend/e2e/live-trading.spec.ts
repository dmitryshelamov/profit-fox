import { type APIRequestContext, expect, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// Acceptance criteria of live trading against the real API and a trading Worker on the Bybit stub: the stub fills orders
// and drops the streams through its control routes, so no order reaches the exchange.

const stub = 'http://127.0.0.1:5296';

async function stubOrders(
  request: APIRequestContext,
): Promise<{ orderLinkId: string; side: string; qty: string; marketUnit: string }[]> {
  return (await (await request.get(`${stub}/control/orders`)).json()) as {
    orderLinkId: string;
    side: string;
    qty: string;
    marketUnit: string;
  }[];
}

test('a bot starts, its fill opens the position, it survives a drop, stops and closes the position', async ({
  page,
  request,
}) => {
  const uid = String(Date.now());
  const account = await request.post('/api/exchange-accounts', {
    data: { name: `Торговый ${uid}`, apiKey: `e2e-valid-${uid}`, apiSecret: 'e2e-secret' },
  });
  expect(account.status()).toBe(201);
  const accountId = ((await account.json()) as { id: string }).id;
  const strategyId = await createStrategy(request, `DCA ${uid}`, reference('dca'));
  const bot = await request.post('/api/bots', {
    data: {
      name: `DCA e2e ${uid}`,
      strategyId,
      category: 'spot',
      symbol: 'BTCUSDT',
      interval: '60',
      balance: 200,
      exchangeAccountId: accountId,
    },
  });
  expect(bot.status()).toBe(201);
  const botId = ((await bot.json()) as { id: string }).id;
  const before = (await stubOrders(request)).length;

  await page.goto(`/bots/${botId}`);
  await page.getByRole('button', { name: 'Запустить' }).click();
  await expect(page.getByRole('dialog')).toContainText(`Торговый ${uid}`);
  await page.getByRole('dialog').getByRole('button', { name: 'Запустить' }).click();
  await expect(page.getByText('Запущен')).toBeVisible();

  await expect
    .poll(async () => (await stubOrders(request)).length, { timeout: 30_000 })
    .toBe(before + 1);
  const entry = (await stubOrders(request)).at(-1)!;
  expect(entry.side).toBe('Buy');
  // The entry goes by the quantity of the coin: Bybit would read a bare qty of a market buy as USDT.
  expect(entry.marketUnit).toBe('baseCoin');
  await request.post(`${stub}/control/fill?orderLinkId=${entry.orderLinkId}&price=100000`);
  const current = page.getByRole('region', { name: 'Текущая позиция' });
  await expect(current).toBeVisible({ timeout: 15_000 });
  await expect(current.getByText('открыта')).toBeVisible();

  await request.post(`${stub}/control/drop`);
  await expect(page.getByText('Запущен')).toBeVisible();

  await page.getByRole('button', { name: 'Остановить' }).click();
  await expect(page.getByText('Остановлен')).toBeVisible({ timeout: 15_000 });
  await expect(current.getByText('открыта')).toBeVisible();

  await page.getByRole('button', { name: 'Закрыть позицию' }).click();
  await expect(page.getByRole('dialog')).toContainText('Продажа по рынку');
  await page.getByRole('dialog').getByRole('button', { name: 'Закрыть позицию' }).click();
  await expect
    .poll(async () => (await stubOrders(request)).length, { timeout: 30_000 })
    .toBe(before + 2);
  const exit = (await stubOrders(request)).at(-1)!;
  expect(exit.side).toBe('Sell');
  await request.post(`${stub}/control/fill?orderLinkId=${exit.orderLinkId}&price=101000`);
  await expect(page.getByRole('region', { name: 'Закрытые позиции' })).toBeVisible({
    timeout: 15_000,
  });
});
