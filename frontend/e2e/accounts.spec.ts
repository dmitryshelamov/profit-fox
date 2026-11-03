import { expect, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// Acceptance criteria of exchange accounts against the real API and the Bybit stub, whose key `e2e-valid-<uid>` opens a
// unified account that trades spot and linear and holds 1000 USDT and 0.01 BTC.

test('an account is added, shows its balance, takes a bot and cannot be deleted then', async ({
  page,
  request,
}) => {
  const uid = String(Date.now());

  await page.goto('/accounts');
  await page.getByRole('link', { name: '+ Новый аккаунт' }).click();
  await page.getByLabel('Название').fill('Проверка e2e');
  await page.getByLabel('API key').fill(`e2e-valid-${uid}`);
  await page.getByLabel('API secret').fill('e2e-secret');
  await page.getByRole('button', { name: 'Добавить аккаунт' }).click();

  await expect(page.getByRole('heading', { name: /Проверка e2e/ })).toBeVisible();
  await expect(page.getByText(uid, { exact: true })).toBeVisible();
  await expect(page.getByLabel('Secret скрыт')).toHaveText('••••••••');
  await expect(page.getByText('e2e-secret')).toHaveCount(0);

  await page.getByRole('button', { name: 'Показать баланс' }).click();
  const balance = page.getByRole('table', { name: 'Баланс' });
  await expect(balance.getByText('USDT')).toBeVisible();
  await expect(balance.getByText('1 675.31')).toBeVisible();

  const accountId = page.url().split('/').pop()!;
  const strategyId = await createStrategy(request, `EMA ${uid}`, reference('ema'));
  const bot = await request.post('/api/bots', {
    data: {
      name: `BTC e2e ${uid}`,
      strategyId,
      category: 'spot',
      symbol: 'BTCUSDT',
      interval: '60',
      balance: 1000,
      exchangeAccountId: accountId,
    },
  });
  expect(bot.status()).toBe(201);

  await page.goto('/bots');
  const row = page.getByRole('row', { name: new RegExp(`BTC e2e ${uid}`) });
  await expect(row.getByText('Проверка e2e')).toBeVisible();

  await page.goto(`/accounts/${accountId}`);
  await page.getByRole('button', { name: 'Удалить' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Удалить' }).click();
  await expect(page.getByRole('alert')).toHaveText(`Аккаунт используют боты: BTC e2e ${uid}.`);
});

test('a key with the withdrawal permission is rejected at the key field', async ({ page }) => {
  await page.goto('/accounts/new');
  await page.getByLabel('Название').fill('С выводом');
  await page.getByLabel('API key').fill('e2e-withdraw');
  await page.getByLabel('API secret').fill('e2e-secret');
  await page.getByRole('button', { name: 'Добавить аккаунт' }).click();

  await expect(page.getByText(/can withdraw funds/)).toBeVisible();
  await expect(page.getByLabel('API secret')).toHaveValue('');
});
