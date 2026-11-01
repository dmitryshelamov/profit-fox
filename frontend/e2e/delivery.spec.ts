import { expect, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// Scenario "Обновление страницы редактора".
test('reopens the editor when its page is reloaded', async ({ page, request }) => {
  const id = await createStrategy(request, `EMA ${Date.now()}`, reference('ema'));

  await page.goto(`/strategies/${id}`);
  await page.reload();

  await expect(page.getByRole('group', { name: 'Свеча' })).toBeVisible();
});

// Scenario "Запрос к API через адрес приложения".
test('answers the API from the address of the application', async ({ request }) => {
  const response = await request.get('/api/strategies');

  expect(response.status()).toBe(200);
  expect(Array.isArray(await response.json())).toBe(true);
});

// Scenario "Новая версия": the page is asked for every time, built files are kept.
test('keeps built files and asks for the page every time', async ({ request }) => {
  const page = await request.get('/strategies');
  const asset = /\/assets\/[^"]+\.js/.exec(await page.text())![0];

  expect(page.headers()['cache-control']).toBe('no-cache');
  expect((await request.get(asset)).headers()['cache-control']).toBe(
    'public, max-age=31536000, immutable',
  );
});
