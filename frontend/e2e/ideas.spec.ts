import { expect, type Locator, test } from '@playwright/test';

import { seedCalibration, seedIdeas } from './seed';

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

  // A paper auto on «Импульс» and «Ранний импульс» is on before the impulse: it takes the ideas of it on paper, with no order.
  const uid = String(Date.now());
  const account = await request.post('/api/exchange-accounts', {
    data: { name: `Учебный ${uid}`, apiKey: `e2e-valid-${uid}`, apiSecret: 'e2e-secret' },
  });
  expect(account.status()).toBe(201);
  const paperAuto = await request.post('/api/idea-autos', {
    data: {
      name: `Учебный импульс ${uid}`,
      accountId: ((await account.json()) as { id: string }).id,
      amount: 30,
      setups: ['impulse', 'early_impulse'],
      types: ['levels', 'target_2r', 'beyond_extreme', 'pullback'],
      intervals: ['5', '15', '60', '240'],
      side: null,
      category: null,
      badge: null,
      minRiskReward: 0,
      minCases: 0,
    },
  });
  expect(paperAuto.status()).toBe(201);
  const paperAutoId = ((await paperAuto.json()) as { id: string }).id;
  expect(
    (
      await request.put(`/api/idea-autos/${paperAutoId}/mode`, { data: { mode: 'paper' } })
    ).status(),
  ).toBe(204);
  const ordersBefore = (
    (await (await request.get(`${stub}/control/orders`)).json()) as {
      symbol: string;
      orderLinkId: string;
    }[]
  ).filter((order) => order.symbol === 'SOLUSDT' && order.orderLinkId.startsWith('i')).length;

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
        const { items } = (await (await request.get('/api/ideas')).json()) as {
          items: { symbol: string; category: string }[];
        };

        return items.some((idea) => idea.symbol === 'SOLUSDT' && idea.category === 'linear');
      },
      { timeout: 300_000, intervals: [10_000] },
    )
    .toBe(true);

  await page.getByRole('link', { name: 'Идеи', exact: true }).click();
  await expect(page).toHaveURL(/\/ideas$/);
  const ideasOfSol = page
    .locator('tbody tr')
    .filter({ hasText: 'linear' })
    .filter({ hasText: 'SOLUSDT' });
  const idea = ideasOfSol.first();
  await expect(idea).toBeVisible();
  await page.getByRole('textbox', { name: 'Сумма на идею' }).fill('10');
  await expect(idea).toContainText(/\+\d+\.\d{2} \/ −\d+\.\d{2}/);

  // A click on «R:R» sorts the list by it, the highest first; the second click turns it round.
  const riskReward = page.getByRole('columnheader', { name: /R:R/ });
  const riskRewards = async () =>
    (
      await page.locator('tbody tr td:nth-child(10) > span > span:first-child').allTextContents()
    ).map(Number);
  // The header changes at once, the rows when the answer comes: wait until they stand in the order.
  const standsIn = (order: (a: number, b: number) => number) => async () => {
    const values = await riskRewards();

    return values.every((value, index) => index === 0 || order(values[index - 1]!, value) <= 0);
  };
  await riskReward.getByRole('button').click();
  await expect(riskReward).toHaveAttribute('aria-sort', 'descending');
  await expect.poll(standsIn((a, b) => b - a)).toBe(true);
  await riskReward.getByRole('button').click();
  await expect(riskReward).toHaveAttribute('aria-sort', 'ascending');
  await expect.poll(standsIn((a, b) => a - b)).toBe(true);

  // An open idea, so that its row on the pair page is not in the closed group of the expired ones.
  await ideasOfSol
    .filter({ hasText: /Ждёт входа|Активна/ })
    .first()
    .click();

  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?idea=/);
  const hub = page.getByRole('region', { name: 'Идеи пары' });
  const selected = hub
    .getByRole('row')
    .filter({ has: page.getByRole('button', { pressed: true }) });
  await expect(selected).toHaveCount(1);
  const chart = page.getByRole('region', { name: 'График' });
  await expect(chart.locator('figure')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Снять выбор' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Своя идея' })).toBeVisible();

  // The chart goes across the page above the table of the ideas.
  const chartBox = (await chart.boundingBox())!;
  const hubBox = (await hub.boundingBox())!;
  expect(chartBox.y + chartBox.height).toBeLessThanOrEqual(hubBox.y);
  expect(chartBox.width).toBe(hubBox.width);

  // The row of the idea has the profit and the loss in USDT of the same amount per idea.
  await expect(selected).toContainText(/\+\d+\.\d{2} \/ −\d+\.\d{2}/);

  // Selecting an idea, removing the selection and switching the interval leave the page where it is.
  // The page is scrolled to just above a part of it, and away from the top.
  const scrollAbove = async (part: Locator) => {
    const top = await part.evaluate((element) => element.getBoundingClientRect().top + scrollY);
    await page.evaluate((y) => window.scrollTo(0, y - 80), top);
    const y = await page.evaluate(() => window.scrollY);
    expect(y).toBeGreaterThan(0);

    return y;
  };
  let scrolled = await scrollAbove(hub);
  const stays = async () => {
    await page.waitForTimeout(500);
    expect(await page.evaluate(() => window.scrollY)).toBe(scrolled);
  };

  await page.getByRole('button', { name: 'Снять выбор' }).click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT$/);
  await expect(page.getByRole('button', { name: 'Снять выбор' })).toHaveCount(0);
  await stays();

  // The first row under the header: an open idea.
  await hub.getByRole('row').nth(1).click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?idea=/);
  await stays();

  // The intervals are on the chart above the ideas: a click on one out of view would scroll to it first.
  const intervals = page.getByRole('group', { name: 'Интервал' });
  scrolled = await scrollAbove(intervals);
  await intervals.getByRole('button', { name: '15 мин', exact: true }).click();
  await expect(page).toHaveURL(/interval=15/);
  await stays();

  await page.getByRole('button', { name: 'Снять выбор' }).click();
  await expect(page).toHaveURL(/\/market\/linear\/SOLUSDT\?interval=15$/);

  // A saved filter is saved and applied: only the pumped pair grew over 1 % in an hour.
  // «Рынок» opens with the setup chip and its interval chosen above; this part starts from all the pairs.
  await page.goto('/market');
  await expect(
    page.getByRole('group', { name: 'Сетап', exact: true }).getByRole('button', { pressed: true }),
  ).toContainText(setupLabels[setup]!);
  await expect(rows.filter({ hasText: 'linear' }).filter({ hasText: 'SOLUSDT' })).toHaveCount(1);
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);
  await page
    .getByRole('group', { name: 'Сетап', exact: true })
    .getByRole('button', { name: /^Все/ })
    .click();
  await expect(rows).toHaveCount(6);
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

  // The filter is kept on the server and the choice of it in the browser: a reload opens the market with it applied.
  await page.reload();
  await expect(page.getByRole('button', { name: 'Рост за час' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(rows.first()).toContainText('SOLUSDT');
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);

  // Taking it off shows all the pairs again, and choosing it applies it once more.
  await page.getByRole('button', { name: 'Рост за час' }).click();
  await expect(rows).toHaveCount(6);
  await page.getByRole('button', { name: 'Рост за час' }).click();
  await expect(rows.filter({ hasNotText: 'SOLUSDT' })).toHaveCount(0);

  // The paper auto took the impulse in the minute it was born: its trade is in the journal among the paper ones, without orders, and the
  // stub has no order of a trade by an idea on SOLUSDT.
  await page.goto('/idea-trades');
  await page
    .getByRole('radiogroup', { name: 'Кто взял' })
    .getByRole('radio', { name: 'Учебные' })
    .click();
  const paperRow = page.locator('tr[data-origin="paper"]').filter({ hasText: 'SOLUSDT' }).first();
  await expect(paperRow).toBeVisible({ timeout: 60_000 });
  await expect(paperRow).toContainText(`Учебный импульс ${uid}`);
  await expect(paperRow.getByRole('button')).toHaveCount(0);
  const open = page
    .getByRole('table', { name: 'Сделки в работе' })
    .locator('tr[data-origin="paper"]');

  if ((await open.count()) > 0) {
    await expect(open.first()).toContainText('без ордеров');
  }

  const ordersAfter = (
    (await (await request.get(`${stub}/control/orders`)).json()) as {
      symbol: string;
      orderLinkId: string;
    }[]
  ).filter((order) => order.symbol === 'SOLUSDT' && order.orderLinkId.startsWith('i')).length;
  expect(ordersAfter).toBe(ordersBefore);
  expect(
    (await request.put(`/api/idea-autos/${paperAutoId}/mode`, { data: { mode: 'off' } })).status(),
  ).toBe(204);

  expect(errors).toEqual([]);
});

// The lists come from the server a page at a time. The ideas are put into the database of the run, since the scanner makes
// too few to fill a page: 120 closed ideas of pairs the market lacks, with a risk to reward of the prices from 11 to 130, and three of
// BTCUSDT (see `seedIdeas`).
test('ideas come a page at a time: the page is in the address and survives Back, a sort orders all pages, the size is kept, a saved filter and the counter of the editor are counted by the server', async ({
  page,
  request,
}) => {
  test.setTimeout(600_000);
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  // The market conditions of the filter need the snapshot of the scanner.
  await page.goto('/market');
  await expect(page.locator('tbody tr')).toHaveCount(6, { timeout: 180_000 });
  await seedIdeas(120);
  const { total } = (await (await request.get('/api/ideas')).json()) as { total: number };
  expect(total).toBeGreaterThanOrEqual(123);

  await page.goto('/ideas');
  const rows = page.locator('tbody tr');
  const riskRewards = async () =>
    (
      await page.locator('tbody tr td:nth-child(10) > span > span:first-child').allTextContents()
    ).map(Number);
  const range = (from: number, to: number) => page.getByText(`${from}–${to} из ${total}`);
  await expect(range(1, 50)).toBeVisible();
  await expect(rows).toHaveCount(50);

  // Turning the page writes it into the address; Back from a pair brings the page back.
  await page.getByRole('button', { name: 'Следующая страница' }).click();
  await expect(page).toHaveURL(/\/ideas\?page=2$/);
  await expect(range(51, 100)).toBeVisible();
  await rows.first().click();
  await expect(page).toHaveURL(/\/market\//);
  await page.goBack();
  await expect(page).toHaveURL(/\/ideas\?page=2$/);
  await expect(range(51, 100)).toBeVisible();

  // A sort goes back to the first page and orders all the ideas, not those of the page.
  const riskReward = page.getByRole('columnheader', { name: /R:R/ });
  await riskReward.getByRole('button').click();
  await expect(riskReward).toHaveAttribute('aria-sort', 'descending');
  await expect(page).toHaveURL(/\/ideas$/);
  await expect(range(1, 50)).toBeVisible();
  await expect.poll(async () => (await riskRewards())[0]).toBe(180.07);
  const first = await riskRewards();
  expect(first).toEqual([...first].sort((a, b) => b - a));
  // The risk to reward after the fees of the stub: 200 and 130 of the prices come to 180.07 and 166.47 (BTCUSDT) and 117.01.
  expect(first.slice(0, 3)).toEqual([180.07, 166.47, 117.01]);
  await page.getByRole('button', { name: 'Следующая страница' }).click();
  await expect(range(51, 100)).toBeVisible();
  const second = await riskRewards();
  expect(second).toEqual([...second].sort((a, b) => b - a));
  expect(Math.max(...second)).toBeLessThanOrEqual(Math.min(...first));

  // The size is kept in the browser: after a reload the list has pages of 100.
  await page.getByRole('combobox', { name: 'На странице' }).selectOption('100');
  await expect(range(1, 100)).toBeVisible();
  await page.reload();
  await expect(page.getByRole('combobox', { name: 'На странице' })).toHaveValue('100');
  await expect(range(1, 100)).toBeVisible();
  await expect(rows).toHaveCount(100);

  // A filter of both places with a condition of the market and one of the idea: the idea has to be a linear one with a risk
  // to reward after the fees of 150, and its pair has to be in the snapshot.
  const filter = await request.post('/api/saved-filters', {
    data: {
      name: 'Линейные от 150',
      place: 'both',
      conditions: [
        { field: 'category', operator: '=', value: 'linear', to: null },
        { field: 'riskReward', operator: '>=', value: '150', to: null },
      ],
    },
  });
  expect(filter.status()).toBe(201);
  await page.reload();
  await page.getByRole('button', { name: 'Линейные от 150' }).click();
  await expect(page.getByText('1 идея', { exact: true })).toBeVisible();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText('BTCUSDT');
  await expect(rows.first()).toContainText('linear');

  await page.goto('/market');
  await expect(page.getByRole('button', { name: 'Линейные от 150' })).toHaveAttribute(
    'aria-pressed',
    'false',
  );
  await page.getByRole('button', { name: 'Линейные от 150' }).click();
  await expect(page.getByText('3 из 6 инструментов')).toBeVisible();
  await expect(rows).toHaveCount(3);
  await expect(rows.filter({ hasText: 'spot' })).toHaveCount(0);

  // The counter of the editor is what the server counts for the conditions typed.
  const fit = await request.post('/api/saved-filters/fit', {
    data: { conditions: [{ field: 'riskReward', operator: '>', value: '150', to: null }] },
  });
  const { pairs, ideas } = (await fit.json()) as { pairs: number; ideas: number };
  expect(ideas).toBeGreaterThanOrEqual(2);
  await page.getByRole('button', { name: '+ Новый фильтр' }).click();
  const editor = page.getByRole('dialog', { name: 'Редактор фильтра' });
  await editor.getByRole('button', { name: '+ Условие' }).click();
  await editor.getByRole('button', { name: 'Поле' }).click();
  await editor.getByRole('menuitemradio', { name: 'R:R' }).click();
  await editor.getByRole('textbox', { name: 'Значение' }).fill('150');
  await expect(
    editor.getByText(new RegExp(`^Подходит: ${pairs} \\S+ · ${ideas} \\S+$`)),
  ).toBeVisible();

  expect(errors).toEqual([]);
});

// The calibration is a report the scanner keeps; the run puts one into its database, since one day of history checks no case.
test('the link «Калибровка» of the ideas opens the calibration with its tiles and tables, and the scope and the window go to the address', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await seedCalibration();

  await page.goto('/ideas');
  await page.getByRole('link', { name: 'Калибровка' }).click();

  await expect(page).toHaveURL(/\/ideas\/calibration$/);
  await expect(page.getByRole('region', { name: 'Оценка Брайера' })).toContainText(
    '0.207 против 0.219',
  );
  await expect(page.getByRole('region', { name: 'Оценка Брайера' })).toContainText(
    'лучше на 5.5 %',
  );
  const probabilities = page.getByRole('table', { name: 'Вероятность' });
  await expect(probabilities.getByRole('row', { name: /30–40 %/ })).toContainText('640');
  await expect(page.getByRole('table', { name: 'Ожидание' })).toBeVisible();
  await expect(
    page.getByRole('table', { name: 'Значки' }).getByRole('row', { name: /▲/ }),
  ).toBeVisible();
  await expect(
    page.getByRole('table', { name: 'Старая модель → новая' }).getByRole('row', { name: /Старая/ }),
  ).toContainText('0.231');
  await expect(
    page
      .getByRole('table', { name: 'По своим данным' })
      .getByRole('row', { name: /мало своих данных/ }),
  ).toContainText('0.212');

  // The report after the rules changed has 120 checked cases of the 640; the scope goes to the address.
  await page.getByRole('button', { name: 'После изменения правил' }).click();
  await expect(page).toHaveURL(/\/ideas\/calibration\?scope=fresh$/);
  await expect(page.getByText(/Проверено 120 кейсов/)).toBeVisible();
  await page.getByRole('button', { name: 'Все случаи' }).click();
  await expect(page).toHaveURL(/\/ideas\/calibration$/);

  await page.getByRole('button', { name: 'за 30 дн' }).click();
  await expect(page).toHaveURL(/\/ideas\/calibration\?days=30$/);
  await expect(page.getByRole('button', { name: 'за 30 дн' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );

  expect(errors).toEqual([]);
});
