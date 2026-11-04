import { expect, test } from '@playwright/test';

// Acceptance of the market against the real API and scanner on the Bybit stub: the scanner keeps BTCUSDT, ETHUSDT and
// SOLUSDT in both categories with a day of history, so its first snapshot comes after the first closed minute. In the
// stub only SOLUSDT grows over an hour; the others change by nothing.

test('the market shows the pairs of the scanner, sorts by the change over an hour and opens a pair with its chart', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await page.goto('/market');

  const rows = page.locator('tbody tr');
  // The table reads the snapshot every 15 seconds; the first one comes once a minute has closed.
  await expect(rows).toHaveCount(6, { timeout: 180_000 });

  for (const symbol of ['BTCUSDT', 'ETHUSDT', 'SOLUSDT']) {
    await expect(page.getByRole('link', { name: symbol, exact: true })).toHaveCount(2);
  }

  // The table opens sorted by the change over an hour, growth first; a click turns it the other way.
  await expect(rows.first().getByRole('link')).toHaveText('SOLUSDT');

  await page.getByRole('button', { name: 'Изменение за 1ч', exact: true }).click();
  await expect(rows.last().getByRole('link')).toHaveText('SOLUSDT');
  await expect(rows.first().getByRole('link')).not.toHaveText('SOLUSDT');

  await rows.filter({ hasText: 'linear' }).filter({ hasText: 'SOLUSDT' }).click();

  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT/);
  await expect(page.getByRole('heading', { name: 'SOLUSDT' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'График' }).locator('figure')).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Открытый интерес' }).locator('figure'),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Фандинг' }).locator('figure')).toBeVisible();
});
