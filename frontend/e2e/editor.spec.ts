import { expect, type Page, test } from '@playwright/test';

import {
  addNode,
  createNode,
  createStrategy,
  type Definition,
  normalized,
  reference,
  selectNode,
} from './api';

const save = (page: Page) => page.getByRole('button', { name: 'Сохранить' });

async function wire(page: Page, node: string, input: string, from: string) {
  await selectNode(page, node);
  await page.getByRole('combobox', { name: `Источник ${input}` }).selectOption(from);
}

async function parameter(
  page: Page,
  name: string,
  limits: { default: string; min: string; max?: string; integer?: boolean },
) {
  const card = page.getByRole('region', { name: 'Стратегия' });
  await card.getByRole('button', { name: '+ Добавить' }).click();
  await card.getByRole('textbox', { name: 'Имя параметра' }).last().fill(name);
  await card.getByRole('textbox', { name: 'Имя параметра' }).last().press('Tab');

  for (const field of ['default', 'min', 'max'] as const) {
    const value = limits[field];

    if (value !== undefined) {
      await card.getByRole('textbox', { name: `${field} ${name}` }).fill(value);
      await card.getByRole('textbox', { name: `${field} ${name}` }).press('Tab');
    }
  }

  if (limits.integer) {
    await card.getByRole('checkbox', { name: 'целое' }).last().check();
  }
}

// Scenario "Сборка EMA с нуля".
test('builds EMA from scratch and saves it as the reference', async ({ page, request }) => {
  await page.goto('/strategies');
  await page.getByRole('button', { name: '+ Новая стратегия' }).click();
  await expect(page.getByRole('group', { name: 'Параметры' })).toBeVisible();

  await page.getByRole('button', { name: 'Параметры', exact: true }).click();
  await page
    .getByRole('region', { name: 'Стратегия' })
    .getByRole('textbox', { name: 'Название' })
    .fill('EMA с нуля');
  await parameter(page, 'fast', { default: '12', min: '2', max: '200', integer: true });
  await parameter(page, 'slow', { default: '26', min: '2', max: '400', integer: true });
  await parameter(page, 'amount', { default: '100', min: '0' });
  await page
    .getByRole('region', { name: 'Стратегия' })
    .getByRole('button', { name: 'Закрыть' })
    .click();

  for (const title of [
    'Свеча',
    'Позиция',
    'Пересечение EMA',
    'НЕ',
    'И',
    'Рыночный ордер',
    'Закрыть позицию',
  ]) {
    await addNode(page, title);
  }

  await selectNode(page, 'market-order1');
  await page
    .getByRole('radiogroup', { name: 'Размер' })
    .getByRole('radio', { name: 'Сумма' })
    .click();
  await wire(page, 'ema-cross1', 'source', 'candle1.close');
  await wire(page, 'ema-cross1', 'fast', 'params.fast');
  await wire(page, 'ema-cross1', 'slow', 'params.slow');
  await wire(page, 'not1', 'value', 'position1.hasPosition');
  await wire(page, 'and1', 'a', 'ema-cross1.up');
  await wire(page, 'and1', 'b', 'not1.result');
  await wire(page, 'market-order1', 'when', 'and1.result');
  await wire(page, 'market-order1', 'size', 'params.amount');
  await wire(page, 'close-position1', 'when', 'ema-cross1.down');

  await expect(save(page)).toBeEnabled();
  await save(page).click();
  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]{36}$/);

  const id = page.url().split('/').at(-1)!;
  const saved = (await (await request.get(`/api/strategies/${id}`)).json()) as {
    definition: Definition;
  };
  expect(normalized(saved.definition)).toEqual(normalized(reference('ema')));
});

// Scenario "Эталонные стратегии открываются полностью".
for (const name of ['ema', 'ema-short', 'dca', 'ladder'] as const) {
  test(`opens the reference strategy ${name} with all nodes and wires`, async ({
    page,
    request,
  }) => {
    const definition = reference(name);
    const id = await createStrategy(request, `${name} ${Date.now()}`, definition);

    await page.goto(`/strategies/${id}`);

    await expect(page.locator('.react-flow__node')).toHaveCount(definition.nodes.length);
    await expect(page.locator('.react-flow__edge')).toHaveCount(definition.wires?.length ?? 0);
  });
}

// Scenarios "Неподключённый вход" and "Есть ошибка".
test('marks an unconnected input and blocks saving', async ({ page, request }) => {
  const id = await createStrategy(request, `EMA ${Date.now()}`, reference('ema'));
  await page.goto(`/strategies/${id}`);

  await wire(page, 'enter', 'when', '');

  await expect(page.getByRole('group', { name: 'Рыночный ордер' })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await expect(page.getByRole('button', { name: /Ошибки/ })).toContainText('1');
  await expect(save(page)).toBeDisabled();
});

// Scenario "Восстановление после закрытия вкладки".
test('restores a draft after the page is reloaded', async ({ page, request }) => {
  const id = await createStrategy(request, `EMA ${Date.now()}`, reference('ema'));
  await page.goto(`/strategies/${id}`);
  await selectNode(page, 'enter');
  await page
    .getByRole('radiogroup', { name: 'Размер' })
    .getByRole('radio', { name: 'Объём' })
    .click();
  await expect(page.getByRole('group', { name: 'Рыночный ордер' })).toContainText(
    'Покупка · Объём',
  );
  await page.waitForTimeout(600);

  page.on('dialog', (dialog) => void dialog.accept());
  await page.reload();

  const offer = page.getByRole('dialog', { name: 'Найден несохранённый черновик' });
  await offer.getByRole('button', { name: 'Восстановить' }).click();
  await expect(page.getByRole('group', { name: 'Рыночный ордер' })).toContainText(
    'Покупка · Объём',
  );
  await expect(save(page)).toBeEnabled();
});

// Scenario "Ломающая правка составного узла": the check finds it before the save.
test('names the strategy a breaking edit of a composite node would break', async ({
  page,
  request,
}) => {
  const node = await createNode(request, `Фильтр тренда ${Date.now()}`, {
    formatVersion: 1,
    inputs: [{ name: 'source', type: 'number' }],
    outputs: [{ name: 'aboveTrend', type: 'boolean' }],
    nodes: [
      { id: 'in', kind: 'inputs' },
      { id: 'ema', kind: 'ema', inputs: { period: 200 } },
      { id: 'above', kind: 'compare', settings: { operation: 'gt' } },
      { id: 'out', kind: 'outputs' },
    ],
    wires: [
      { from: 'in.source', to: 'ema.source' },
      { from: 'in.source', to: 'above.a' },
      { from: 'ema.value', to: 'above.b' },
      { from: 'above.result', to: 'out.aboveTrend' },
    ],
  });
  await createStrategy(request, 'EMA trend', {
    direction: 'long',
    parameters: [],
    nodes: [
      { id: 'candle', kind: 'candle' },
      { id: 'trend', kind: 'composite', ref: node },
      {
        id: 'buy',
        kind: 'market-order',
        settings: { side: 'buy', size: 'quantity' },
        inputs: { size: 1 },
      },
    ],
    wires: [
      { from: 'candle.close', to: 'trend.source' },
      { from: 'trend.aboveTrend', to: 'buy.when' },
    ],
    formatVersion: 1,
  } as Definition);
  await page.goto(`/nodes/${node}`);

  await page.getByRole('button', { name: 'Входы и выходы' }).click();
  await page.getByRole('button', { name: 'Удалить выход aboveTrend' }).click();
  await page.getByRole('button', { name: /Ошибки/ }).click();

  await expect(page.getByRole('region', { name: 'Ошибки' })).toContainText('Стратегия «EMA trend»');
  await expect(save(page)).toBeDisabled();
});

// Scenario "Клонировать и править".
test('clones a built-in node and edits the clone', async ({ page }) => {
  await page.goto('/nodes/ladder');
  await expect(page.getByRole('note')).toContainText('Встроенный узел нельзя править');

  await page.getByRole('button', { name: 'Клонировать и править' }).click();

  await expect(page).toHaveURL(/\/nodes\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('button', { name: 'Добавить узел' })).toBeVisible();
  await expect(save(page)).toBeVisible();
});
