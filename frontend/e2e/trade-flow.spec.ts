import { expect, test } from '@playwright/test';

// Acceptance of the panel «Покупки и продажи» against the real API, scanner and collector on the Bybit stub: the stub sends a trade of every
// pair every half second and has two trades at noon of every closed day in its archive, which the collector rebuilds two days of.

test('the pair page shows the buys and sells of the trades of the stub and the live bar', async ({
  page,
}) => {
  test.setTimeout(300_000);
  await page.goto('/market');
  // The pair page exists once the scanner has saved its first snapshot, and the collector follows the list of the scanner.
  await expect(page.locator('tbody tr')).toHaveCount(6, { timeout: 180_000 });

  await page.goto('/market/linear/SOLUSDT');

  const panel = page.getByRole('region', { name: 'Покупки и продажи' });
  await expect(panel.locator('figure')).toBeVisible();
  await expect(panel.getByRole('group', { name: 'Последний бар' })).toContainText('Покупки', {
    timeout: 120_000,
  });
  await expect(panel.getByText('в реальном времени')).toBeVisible({ timeout: 60_000 });
  await expect(panel.getByRole('alert')).toHaveCount(0);
});
