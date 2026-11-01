import { expect, type Page, test } from '@playwright/test';

import { createStrategy, reference } from './api';

// React Flow picks the multi-selection key by the user agent: Meta on a Mac, Control elsewhere. A Mac agent and Meta
// behave the same on every machine, while Control-click on a Mac opens the context menu.
test.use({
  userAgent:
    'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36',
});

const node = (page: Page, id: string) => page.locator(`.react-flow__node[data-id="${id}"]`);

async function header(page: Page, id: string) {
  const box = (await node(page, id).boundingBox())!;

  return { x: box.x + 30, y: box.y + 12 };
}

test('keeps every node of a dragged selection where it was dropped', async ({ page, request }) => {
  const id = await createStrategy(request, `EMA ${Date.now()}`, reference('ema'));
  await page.goto(`/strategies/${id}`);
  await expect(page.locator('.react-flow__node')).toHaveCount(8);

  await node(page, 'flat').click({ position: { x: 30, y: 12 } });
  await page.keyboard.down('Meta');
  await node(page, 'entry').click({ position: { x: 30, y: 12 } });
  await page.keyboard.up('Meta');
  await expect(page.locator('.react-flow__node.selected')).toHaveCount(2);
  const before = { flat: await header(page, 'flat'), entry: await header(page, 'entry') };
  await page.mouse.move(before.flat.x, before.flat.y);
  await page.mouse.down();
  await page.mouse.move(before.flat.x + 80, before.flat.y + 60, { steps: 10 });
  await page.mouse.up();
  await page.waitForTimeout(300);

  // The drag threshold takes the first steps, so the distance is checked loosely; both nodes move the same.
  const after = { flat: await header(page, 'flat'), entry: await header(page, 'entry') };
  expect(after.flat.x - before.flat.x).toBeGreaterThan(50);
  expect(after.entry.x - before.entry.x).toBeCloseTo(after.flat.x - before.flat.x, 0);
  expect(after.entry.y - before.entry.y).toBeCloseTo(after.flat.y - before.flat.y, 0);
});
