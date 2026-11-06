import { expect, test } from '@playwright/test';

// Scenario "Стратегия из шаблона": a strategy made from a template is saved and backtested like any other. The candles are
// those of the Bybit stub (e2e/bybit-stub.ts), a saw-tooth that never changes.

const period = { from: '2025-02-03 00:00', to: '2025-02-13 00:00' };

test('creates a strategy from the RSI template, saves it and runs a backtest of it', async ({
  page,
  request,
}) => {
  const name = 'Отскок RSI';

  await page.goto('/strategies');
  await page.getByRole('button', { name: '+ Новая стратегия' }).click();
  const dialog = page.getByRole('dialog', { name: 'Новая стратегия' });
  await expect(dialog.getByRole('button', { name: 'Пустая стратегия' })).toBeVisible();
  await dialog.getByRole('button', { name: /^Отскок RSI/ }).click();

  await expect(page).toHaveURL(/\/strategies\/new\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('group', { name: 'RSI', exact: true })).toBeVisible();
  await expect(page.getByRole('banner', { name: 'Стратегия' })).toContainText(name);
  await expect(page.getByRole('button', { name: 'Сохранить' })).toBeEnabled();
  await page.getByRole('button', { name: 'Сохранить' }).click();

  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]{36}$/);
  const strategyId = page.url().split('/').pop()!;

  const created = await request.post('/api/bots', {
    data: {
      name: `RSI ${Date.now()}`,
      strategyId,
      parameterValues: {},
      category: 'spot',
      symbol: 'BTCUSDT',
      interval: '60',
      balance: 1000,
    },
  });
  expect(created.status()).toBe(201);
  const botId = ((await created.json()) as { id: string }).id;

  await page.goto(`/bots/${botId}/backtests/new`);
  await page.getByLabel('Начало периода').fill(period.from);
  await page.getByLabel('Конец периода').fill(period.to);
  await page.getByRole('button', { name: 'Запустить' }).click();

  await expect(page).toHaveURL(/\/backtests\/[0-9a-f-]{36}$/);
  await expect(page.locator('header').getByText('Готов')).toBeVisible({ timeout: 90_000 });
});
