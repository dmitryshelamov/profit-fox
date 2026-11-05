import { expect, test } from '@playwright/test';

// Acceptance of the trade ideas against the real API and scanner on the Bybit stub. The scanner primes the setups of its
// pairs on the first minute after its warm-up, so a setup that holds at start-up never fires: the stub starts a steep, loud
// rise of SOLUSDT (`/control/pump`) only once the market has a snapshot, and the test waits for the scanner to read it as
// an impulse (setups are read once per closed minute) and to make its ideas.

const stub = 'http://127.0.0.1:5296';
const setupLabels: Record<string, string> = {
  impulse: 'Импульс',
  early_impulse: 'Ранний импульс',
};
const intervalLabels: Record<string, string> = { '5': '5 мин', '15': '15 мин' };

interface Row {
  setups: Record<string, string[]>;
}

test('an impulse of the stub shows in the market chips, has its idea on the pair page and a saved filter finds it', async ({
  page,
  request,
}) => {
  test.setTimeout(1_500_000);
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('/market');

  const rows = page.locator('tbody tr');
  // The table reads the snapshot every 15 seconds; the first one comes once a minute has closed.
  await expect(rows).toHaveCount(6, { timeout: 180_000 });
  expect((await request.get(`${stub}/control/pump?symbol=SOLUSDT&minutes=12`)).ok()).toBe(true);

  // The impulse shows on the market once the scanner has read the pumped minutes: on 5 or on 15 minutes.
  let found: { interval: string; setup: string } | null = null;
  await expect
    .poll(
      async () => {
        const row = (await (await request.get('/api/market/linear/SOLUSDT')).json()) as Row;

        for (const interval of ['5', '15']) {
          const setup = Object.keys(setupLabels).find((code) =>
            row.setups[interval]?.includes(code),
          );

          if (setup) {
            found = { interval, setup };

            return true;
          }
        }

        return false;
      },
      { timeout: 900_000, intervals: [10_000] },
    )
    .toBe(true);
  const { interval, setup } = found!;

  await page.goto('/market');
  await expect(rows.first()).toBeVisible();
  await page
    .getByRole('group', { name: 'ТФ сетапа' })
    .getByRole('button', { name: intervalLabels[interval]!, exact: true })
    .click();
  await page
    .getByRole('group', { name: 'Сетап' })
    .getByRole('button', { name: new RegExp(`^${setupLabels[setup]!} [1-9]`) })
    .click();

  await expect(rows.filter({ hasText: 'linear' }).filter({ hasText: 'SOLUSDT' })).toHaveCount(1);
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);
  await expect(page.getByText(/^\d+ из 6 инструментов$/)).toBeVisible();

  // The scanner makes the ideas of the setup in the same minute cycle.
  await expect
    .poll(
      async () => {
        const ideas = (await (await request.get('/api/ideas')).json()) as {
          symbol: string;
          category: string;
        }[];

        return ideas.some((idea) => idea.symbol === 'SOLUSDT' && idea.category === 'linear');
      },
      { timeout: 300_000, intervals: [10_000] },
    )
    .toBe(true);

  await page.getByRole('link', { name: 'Идеи', exact: true }).click();
  await expect(page).toHaveURL(/\/ideas$/);
  const idea = page
    .locator('tbody tr')
    .filter({ hasText: 'linear' })
    .filter({ hasText: 'SOLUSDT' })
    .first();
  await expect(idea).toBeVisible();
  await page.getByRole('textbox', { name: 'Риск на идею' }).fill('10');
  await expect(idea).toContainText('−10.0');

  // A click on «R:R» sorts the list by it, the highest first; the second click turns it round.
  const riskReward = page.getByRole('columnheader', { name: /R:R/ });
  const riskRewards = async () =>
    (await page.locator('tbody tr td:nth-child(10)').allTextContents()).map(Number);
  await riskReward.getByRole('button').click();
  await expect(riskReward).toHaveAttribute('aria-sort', 'descending');
  expect(await riskRewards()).toEqual([...(await riskRewards())].sort((a, b) => b - a));
  await riskReward.getByRole('button').click();
  await expect(riskReward).toHaveAttribute('aria-sort', 'ascending');
  expect(await riskRewards()).toEqual([...(await riskRewards())].sort((a, b) => a - b));

  await idea.click();

  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?idea=/);
  const selected = page.getByRole('region', { name: 'Идеи пары' }).getByRole('button', {
    pressed: true,
  });
  await expect(selected).toHaveCount(1);
  await expect(page.getByRole('region', { name: 'График' }).locator('figure')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Снять выбор' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Своя идея' })).toBeVisible();

  // The card of the idea has the profit and the loss in USDT of the same risk per idea.
  await expect(selected).toContainText('Прибыль');
  await expect(selected).toContainText('Убыток');
  await expect(selected).toContainText('−10.0');
  await expect(selected).toContainText('USDT');

  // Selecting an idea, removing the selection and switching the interval leave the page where it is.
  const hub = page.getByRole('region', { name: 'Идеи пары' });
  const hubTop = await hub.evaluate((element) => element.getBoundingClientRect().top + scrollY);
  await page.evaluate((top) => window.scrollTo(0, top - 80), hubTop);
  const scrolled = await page.evaluate(() => window.scrollY);
  expect(scrolled).toBeGreaterThan(0);
  const stays = async () => {
    await page.waitForTimeout(500);
    expect(await page.evaluate(() => window.scrollY)).toBe(scrolled);
  };

  await page.getByRole('button', { name: 'Снять выбор' }).click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT$/);
  await expect(page.getByRole('button', { name: 'Снять выбор' })).toHaveCount(0);
  await stays();

  await hub.getByRole('list').first().getByRole('button').first().click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?idea=/);
  await stays();

  await page
    .getByRole('group', { name: 'Интервал' })
    .getByRole('button', { name: '15 мин', exact: true })
    .click();
  await expect(page).toHaveURL(/interval=15/);
  await stays();

  await page.getByRole('button', { name: 'Снять выбор' }).click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?interval=15$/);

  // A saved filter is saved and applied: only the pumped pair grew over 1 % in an hour.
  await page.goto('/market');
  await expect(rows.first()).toBeVisible();
  await page.getByRole('button', { name: '+ Новый фильтр' }).click();
  const editor = page.getByRole('dialog', { name: 'Редактор фильтра' });
  await editor.getByRole('textbox', { name: 'Название' }).fill('Рост за час');
  await editor.getByRole('button', { name: '+ Условие' }).click();
  await editor.getByRole('button', { name: 'Поле' }).click();
  await editor.getByRole('menuitemradio', { name: 'Изменение 1ч' }).click();
  await editor.getByRole('textbox', { name: 'Значение' }).fill('1');
  await expect(editor.getByText(/^Подходит: 2 инструмента/)).toBeVisible();
  await editor.getByRole('button', { name: 'Сохранить' }).click();

  await expect(editor).toBeHidden();
  await expect(page.getByLabel('Условия фильтра')).toHaveText('Изменение 1ч > 1 %');
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);
  await expect(rows.first()).toContainText('SOLUSDT');

  // The filter is kept on the server: a reload offers it again, and choosing it applies it.
  await page.reload();
  await expect(rows).toHaveCount(6, { timeout: 60_000 });
  await page.getByRole('button', { name: 'Рост за час' }).click();
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);

  expect(errors).toEqual([]);
});
