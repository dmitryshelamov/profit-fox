import { type APIRequestContext, expect, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// Acceptance criteria of the bot chart against the real API (history read through to the stub, the SignalR hub) and a
// trading Worker on the Bybit stub: the stub sends the current candle and the fills through its control routes.

const stub = 'http://127.0.0.1:5296';
const hour = 3_600_000;

async function stubOrders(request: APIRequestContext): Promise<{ orderLinkId: string }[]> {
  return (await (await request.get(`${stub}/control/orders`)).json()) as { orderLinkId: string }[];
}

test('the chart of a running bot shows history, the current candle live, its closing, a fill and another interval', async ({
  page,
  request,
}) => {
  const uid = String(Date.now());
  const account = await request.post('/api/exchange-accounts', {
    data: { name: `График ${uid}`, apiKey: `e2e-valid-${uid}`, apiSecret: 'e2e-secret' },
  });
  expect(account.status()).toBe(201);
  const accountId = ((await account.json()) as { id: string }).id;
  const strategyId = await createStrategy(request, `DCA график ${uid}`, reference('dca'));
  const bot = await request.post('/api/bots', {
    data: {
      name: `График e2e ${uid}`,
      strategyId,
      category: 'spot',
      symbol: 'BTCUSDT',
      interval: '60',
      balance: 300,
      exchangeAccountId: accountId,
    },
  });
  expect(bot.status()).toBe(201);
  const botId = ((await bot.json()) as { id: string }).id;
  const before = (await stubOrders(request)).length;
  const candleStart = Math.floor(Date.now() / hour) * hour;
  const last = page.getByRole('group', { name: 'Последняя свеча' });

  await page.goto(`/bots/${botId}`);
  await page.getByRole('button', { name: 'Запустить' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Запустить' }).click();
  await expect(page.getByText('Запущен')).toBeVisible();

  // History: the last closed candle of the stub, with its volume of 10.
  await expect(last).toContainText('Объём 10');
  await expect(last).not.toContainText('не закрыта');

  // The current candle: the stub sends it until the page has joined the hub.
  await expect(async () => {
    await request.post(
      `${stub}/control/forming?symbol=BTCUSDT&interval=60&start=${candleStart}&close=101234&volume=7.5`,
    );
    await expect(last).toContainText('не закрыта', { timeout: 1_500 });
  }).toPass({ timeout: 30_000 });
  await expect(page.getByText('в реальном времени')).toBeVisible();
  await expect(last).toContainText('C 101234');
  await expect(last).toContainText('Объём 7.5');

  // The entry fills before the candle closes, which makes the bot buy again. The fill sits on the current candle and lands on the chart within the polling.
  await expect
    .poll(async () => (await stubOrders(request)).length, { timeout: 30_000 })
    .toBe(before + 1);
  const entry = (await stubOrders(request)).at(-1)!;
  await request.post(`${stub}/control/fill?orderLinkId=${entry.orderLinkId}&price=100000`);
  await expect(page.getByRole('img', { name: /График свечей/ })).toHaveAccessibleName(
    /исполнений: 1/,
    { timeout: 15_000 },
  );

  // Its closing: the candle stops being "not closed".
  await request.post(
    `${stub}/control/candle?symbol=BTCUSDT&interval=60&start=${candleStart}&close=101500&volume=9`,
  );
  await expect(last).not.toContainText('не закрыта');
  await expect(last).toContainText('C 101500');

  // Another interval: in the address, no current candle, the note about the minute refresh.
  await page.getByRole('button', { name: '5 мин', exact: true }).click();
  await expect(page).toHaveURL(/interval=5/);
  await expect(page.getByText(/раз в минуту/)).toBeVisible();
  await expect(page.getByText('не закрыта')).toHaveCount(0);
  await expect(page.getByRole('img', { name: /График свечей/ })).toHaveAccessibleName(/5 мин/);

  // The bot stops, so its next purchase does not meet the candles of other tests.
  await page.getByRole('button', { name: 'Остановить' }).click();
  await expect(page.getByText('Остановлен')).toBeVisible({ timeout: 15_000 });
});
