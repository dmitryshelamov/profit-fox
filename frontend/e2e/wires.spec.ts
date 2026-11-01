import { expect, type Page, test } from '@playwright/test';

import { createStrategy, reference } from './api';

const amount = (page: Page) =>
  page.locator('.react-flow__edge[data-id="params.amount->enter.size"]');

async function openEma(page: Page, request: Parameters<typeof createStrategy>[0]) {
  const id = await createStrategy(request, `EMA ${Date.now()}`, reference('ema'));
  await page.goto(`/strategies/${id}`);
  await expect(page.locator('.react-flow__edge')).toHaveCount(9);
}

test('removes a selected wire with Delete', async ({ page, request }) => {
  await openEma(page, request);

  // A point of the wire near its output, where no node covers it.
  const point = await amount(page)
    .locator('.react-flow__edge-interaction')
    .evaluate((path: SVGPathElement) => {
      const local = path.getPointAtLength(path.getTotalLength() * 0.1);
      const matrix = path.getScreenCTM()!;

      return { x: local.x * matrix.a + matrix.e, y: local.y * matrix.d + matrix.f };
    });
  await page.mouse.click(point.x, point.y);
  await expect(amount(page)).toHaveClass(/selected/);
  await page.keyboard.press('Delete');

  await expect(amount(page)).toHaveCount(0);
  await expect(page.locator('.react-flow__node')).toHaveCount(8);
});

test('takes a wire away when its end is pulled off the input and let go on empty canvas', async ({
  page,
  request,
}) => {
  await openEma(page, request);

  const end = amount(page).locator('.react-flow__edgeupdater-target');
  const box = (await end.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(1300, 820, { steps: 10 });
  await page.mouse.up();

  await expect(amount(page)).toHaveCount(0);
  await expect(page.locator('.react-flow__edge')).toHaveCount(8);
});

async function drag(
  page: Page,
  from: { x: number; y: number; width: number; height: number },
  to: { x: number; y: number },
) {
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 10 });
  await page.mouse.up();
}

const sizePin = (page: Page) =>
  page.locator('.react-flow__node[data-id="enter"] .react-flow__handle[data-handleid="size"]');

test('takes a wire away when its input pin is dragged onto empty canvas', async ({
  page,
  request,
}) => {
  await openEma(page, request);

  await drag(page, (await sizePin(page).boundingBox())!, { x: 1300, y: 820 });

  await expect(amount(page)).toHaveCount(0);
  await expect(page.locator('.react-flow__edge')).toHaveCount(8);
});

test('rewires an input whose pin is dragged onto another output', async ({ page, request }) => {
  await openEma(page, request);
  const close = page.locator(
    '.react-flow__node[data-id="candle"] .react-flow__handle[data-handleid="close"]',
  );
  const target = (await close.boundingBox())!;

  await drag(page, (await sizePin(page).boundingBox())!, {
    x: target.x + target.width / 2,
    y: target.y + target.height / 2,
  });

  await expect(amount(page)).toHaveCount(0);
  await expect(page.locator('.react-flow__edge[data-id="candle.close->enter.size"]')).toHaveCount(
    1,
  );
});
