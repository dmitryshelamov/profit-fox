import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// Acceptance criteria of the bot and backtest screens, against the real API and Worker with the candles of the Bybit
// stub (e2e/bybit-stub.ts), which never change: a saw-tooth around the base price listed at 2025-01-01.

const period = { from: '2025-02-03 00:00', to: '2025-02-13 00:00' };
const ranges = {
  fast: { min: 2, max: 4, step: 1 },
  slow: { min: 6, max: 10, step: 4 },
};
const combinations = 6;

interface Metrics {
  return: string;
  closedTrades: number;
  fills: number;
}

interface ResultsPage {
  total: number | string;
  items: {
    combination: number | string;
    parameterValues: Record<string, unknown>;
    metrics: Metrics;
  }[];
}

/** The best combination of the stub's candles, counted once and fixed here: the saw-tooth loses a little on every cycle. */
const best = {
  combination: 0,
  parameterValues: { fast: 2, slow: 6, amount: 100 },
  return: '-0.379810000000',
  closedTrades: 19,
};

async function bestOf(request: APIRequestContext, backtestId: string, sort = 'return') {
  const response = await request.get(
    `/api/backtests/${backtestId}/results?sort=${sort}&order=desc&limit=1`,
  );
  expect(response.status()).toBe(200);

  return ((await response.json()) as ResultsPage).items[0]!;
}

async function createBot(request: APIRequestContext, name: string, interval = '60') {
  const strategyId = await createStrategy(request, `EMA ${name}`, reference('ema'));
  const response = await request.post('/api/bots', {
    data: {
      name,
      strategyId,
      parameterValues: {},
      category: 'spot',
      symbol: 'BTCUSDT',
      interval,
      balance: 1000,
    },
  });
  expect(response.status()).toBe(201);

  return ((await response.json()) as { id: string }).id;
}

async function rangeOf(
  page: Page,
  name: string,
  range: { min: number; max: number; step: number },
) {
  const row = page.getByRole('row').filter({ has: page.getByText(name, { exact: true }) });

  await row.getByRole('button', { name: 'Диапазон' }).click();
  await page.getByLabel(`${name} min`, { exact: true }).fill(String(range.min));
  await page.getByLabel(`${name} max`, { exact: true }).fill(String(range.max));
  await page.getByLabel(`${name} шаг`, { exact: true }).fill(String(range.step));
}

test.describe.configure({ mode: 'serial' });

// Scenarios "Бот создан", "Подсказки по части символа", "Валюта котировки", "Значение вне ограничений".
test('creates a bot with suggested symbol, quote currency and a server error at the field', async ({
  page,
  request,
}) => {
  const name = `Bot ${Date.now()}`;
  await createStrategy(request, `EMA form ${name}`, reference('ema'));

  await page.goto('/bots/new');
  await page.getByLabel('Название').fill(name);
  await page.getByLabel('Стратегия').selectOption({ label: `EMA form ${name}` });
  await page.getByRole('button', { name: 'spot', exact: true }).click();
  await page.getByLabel('Символ').fill('BTC');
  await expect(page.locator('#bot-symbols option[value="BTCUSDT"]')).toHaveText('BTC / USDT');
  await page.getByLabel('Символ').fill('BTCUSDT');
  await expect(page.locator('#bot-balance + span')).toHaveText('USDT');
  await page.getByLabel('Баланс бота').fill('1000');
  await page.getByLabel('fast', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Создать бота' }).click();

  await expect(page.getByRole('alert')).toContainText('ошибок проверки 1');
  await expect(page.locator('#parameter-fast + p')).toBeVisible();
  await expect(page.getByLabel('Название')).toHaveValue(name);
  await expect(page.getByLabel('Баланс бота')).toHaveValue('1000');

  await page.getByLabel('fast', { exact: true }).fill('5');
  await page.getByRole('button', { name: 'Создать бота' }).click();

  await expect(page).toHaveURL(/\/bots\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name, level: 1 })).toBeVisible();
});

let backtestId = '';
let botId = '';

// Scenario "Бэктест с диапазонами": the best combination is exact and does not change between runs.
test('runs a ranged backtest to completion with the exact best combination', async ({
  page,
  request,
}) => {
  botId = await createBot(request, `Ranged ${Date.now()}`);

  await page.goto(`/bots/${botId}/backtests/new`);
  await page.getByLabel('Начало периода').fill(period.from);
  await page.getByLabel('Конец периода').fill(period.to);
  await rangeOf(page, 'fast', ranges.fast);
  await rangeOf(page, 'slow', ranges.slow);
  await expect(page.getByText(`Комбинаций: ${combinations}`)).toBeVisible();
  await page.getByRole('button', { name: 'Запустить' }).click();

  await expect(page).toHaveURL(/\/backtests\/[0-9a-f-]{36}$/);
  backtestId = page.url().split('/').pop()!;
  await expect(page.locator('header').getByText('Готов')).toBeVisible({ timeout: 90_000 });

  const first = await bestOf(request, backtestId);
  const row = page.getByRole('row').nth(1);

  expect(Number(first.combination)).toBe(best.combination);
  expect(first.parameterValues).toEqual(best.parameterValues);
  expect(first.metrics.return).toBe(best.return);
  expect(first.metrics.closedTrades).toBe(best.closedTrades);
  await expect(row).toContainText(`${best.closedTrades}`);
  await expect(page.getByRole('row')).toHaveCount(combinations + 1);

  // The same bot, period and ranges again: a new backtest with the very same best combination.
  const again = await request.post(`/api/bots/${botId}/backtests`, {
    data: {
      from: `${period.from.replace(' ', 'T')}:00Z`,
      to: `${period.to.replace(' ', 'T')}:00Z`,
      parameters: ranges,
    },
  });
  expect(again.status()).toBe(201);
  const secondId = ((await again.json()) as { id: string }).id;

  await expect
    .poll(
      async () =>
        ((await (await request.get(`/api/backtests/${secondId}`)).json()) as { status: string })
          .status,
      { timeout: 90_000 },
    )
    .toBe('Succeeded');
  const second = await bestOf(request, secondId);
  expect({ ...second, calculationTime: null }).toEqual({ ...first, calculationTime: null });
});

// Scenario "Сортировка по метрике".
test('sorts the table by a metric column', async ({ page, request }) => {
  await page.goto(`/backtests/${backtestId}`);
  await expect(page.getByRole('row')).toHaveCount(combinations + 1);

  const sorted = page.getByRole('columnheader', { name: 'Сделки' });
  await sorted.getByRole('button').click();

  await expect(sorted).toHaveAttribute('aria-sort', 'descending');
  const top = await bestOf(request, backtestId, 'closedTrades');
  const row = page.getByRole('row').nth(1);

  await expect(row.getByRole('link')).toHaveText(String(top.combination));
  await expect(row).toContainText(`${top.metrics.closedTrades}`);

  await sorted.getByRole('button').click();
  await expect(sorted).toHaveAttribute('aria-sort', 'ascending');
});

// Scenario "Детали результата".
test('shows the chart and the trades of a result', async ({ page, request }) => {
  const details = await request.get(
    `/api/backtests/${backtestId}/results/${best.combination}/details`,
  );
  expect(details.status()).toBe(200);
  const trades = ((await details.json()) as { trades: unknown[] }).trades.length;

  await page.goto(`/backtests/${backtestId}/results/${best.combination}`);

  await expect(
    page.getByRole('figure', { name: 'Цена и капитал' }).locator('canvas').first(),
  ).toBeVisible();
  await expect(page.getByRole('tab', { name: `Сделки · ${trades}` })).toBeVisible();
  expect(trades).toBeGreaterThan(0);
  const table = page
    .getByRole('table')
    .filter({ has: page.getByRole('columnheader', { name: 'Открытие' }) });
  await expect(table.getByRole('row')).toHaveCount(trades + 1);
});

// Scenario "Сравнение по ссылке".
test('compares two results and opens the same set by its link', async ({ page, browser }) => {
  await page.goto(`/backtests/${backtestId}`);
  await expect(page.getByRole('row')).toHaveCount(combinations + 1);

  await page.getByRole('checkbox', { name: 'Сравнить результат 0' }).check();
  await page.getByRole('checkbox', { name: 'Сравнить результат 1' }).check();
  const bar = page.getByRole('region', { name: 'Сравнение' });
  await expect(bar).toContainText('2 из 6');
  await bar.getByRole('link', { name: 'Сравнить' }).click();

  await expect(page).toHaveURL(/\/compare\?items=/);
  const columns = page.getByRole('columnheader').filter({ hasText: /#\d/ });
  await expect(columns).toHaveCount(2);
  const heads = await columns.allInnerTexts();

  const other = await (await browser.newContext({ timezoneId: 'UTC' })).newPage();
  await other.goto(page.url());
  await expect(other.getByRole('columnheader').filter({ hasText: /#\d/ })).toHaveCount(2);
  expect(await other.getByRole('columnheader').filter({ hasText: /#\d/ }).allInnerTexts()).toEqual(
    heads,
  );
  await other.context().close();
});

// Scenario "Отмена бэктеста".
test('cancels a running backtest', async ({ page, request }) => {
  const id = await createBot(request, `Cancel ${Date.now()}`, '1');

  await page.goto(`/bots/${id}/backtests/new`);
  await rangeOf(page, 'fast', { min: 2, max: 101, step: 1 });
  await rangeOf(page, 'slow', { min: 2, max: 101, step: 1 });
  await expect(page.getByText('Комбинаций: 10 000')).toBeVisible();
  await page.getByRole('button', { name: 'Запустить' }).click();

  await expect(page).toHaveURL(/\/backtests\/[0-9a-f-]{36}$/);
  await page.getByRole('button', { name: 'Отменить', exact: true }).click();
  await page
    .getByRole('dialog', { name: 'Отменить бэктест?' })
    .getByRole('button', { name: 'Отменить бэктест' })
    .click();

  await expect(page.locator('header').getByText('Отменён')).toBeVisible({ timeout: 30_000 });
});
