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
  await expect(expectancy.nth(0)).toHaveText('▲ +0.21 R');
  await expect(expectancy.nth(1)).toHaveText('▲ +1.96 R');
  // The out-of-sample check: 10 of the 40 seeded signals are older than 30 days, too few for a best rule of the selection.
  await expect(page.getByRole('table', { name: 'Проверка вне выборки' })).toContainText(
    'на подборе нет правила с 30 входами',
  );

  await page.getByRole('button', { name: 'Применить' }).click();
  await expect(page.getByText('срок 48 свечей · цель 2R · окно входа 4 свечи')).toBeVisible();
  await expect(page.getByText(/применено/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Применить' })).toBeDisabled();
});
