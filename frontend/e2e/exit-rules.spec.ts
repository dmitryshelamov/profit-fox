import { expect, test } from '@playwright/test';

import { removeExitRules, seedSignals } from './seed';

// Acceptance of the screen «Срок жизни и цель» against the real API on the signals of the replay a seed stores: the lifetime of
// «Откат» on 15 minutes moves from 24 candles, at which the seeded signals expire, to 48, at which they take their target.

test.afterEach(async () => {
  await removeExitRules();
});

test('the lifetime of «Откат» on 15 minutes is previewed and applied', async ({ page }) => {
  await seedSignals();
  await page.goto('/ideas/exit-rules');

  await page.getByRole('group', { name: 'Тип' }).getByRole('button', { name: 'Откат' }).click();
  await page.getByRole('group', { name: 'ТФ' }).getByRole('button', { name: '15 мин' }).click();
  await expect(page.getByText('срок 24 свечи · цель 2R · окно входа 4 свечи')).toBeVisible();

  await page.getByRole('combobox', { name: 'Срок жизни, свечей' }).selectOption('48');
  const expectancy = page
    .getByRole('table', { name: 'Все 90 дней' })
    .getByRole('row', { name: /Ожидание на вход/ })
    .getByRole('cell');
  // After the maker fee of the limit entry at 100 and the taker fee and the slippage of linear (0.05 %) on the exit: the close 100.5
  // sells at 100.44975, (0.44975 − 0.02 − 0.0552474) / 2; the target 104 sells at 103.948, (3.948 − 0.02 − 0.0571714) / 2.
  await expect(expectancy.nth(0)).toHaveText('▲ +0.19 R');
  await expect(expectancy.nth(1)).toHaveText('▲ +1.94 R');

  await page.getByRole('button', { name: 'Применить' }).click();
  await expect(page.getByText('срок 48 свечей · цель 2R · окно входа 4 свечи')).toBeVisible();
  await expect(page.getByText(/применено/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Применить' })).toBeDisabled();
});
