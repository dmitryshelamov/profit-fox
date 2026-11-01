import { readFileSync } from 'node:fs';

import { type APIRequestContext, expect, type Page } from '@playwright/test';

export interface Definition {
  direction?: string;
  parameters?: unknown[];
  nodes: { id: string; kind: string; ref?: string; settings?: unknown; inputs?: unknown }[];
  wires?: { from: string; to: string }[];
  layout?: unknown;
}

/** A reference strategy of the backend tests: EMA, EMA Short, DCA or Ladder. */
export function reference(name: 'ema' | 'ema-short' | 'dca' | 'ladder'): Definition {
  return JSON.parse(
    readFileSync(
      new URL(
        `../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/${name}.json`,
        import.meta.url,
      ),
      'utf8',
    ),
  ) as Definition;
}

export async function createStrategy(
  request: APIRequestContext,
  name: string,
  definition: Definition,
): Promise<string> {
  const response = await request.post('/api/strategies', { data: { name, definition } });
  expect(response.status()).toBe(201);

  return ((await response.json()) as { id: string }).id;
}

export async function createNode(
  request: APIRequestContext,
  name: string,
  definition: unknown,
): Promise<string> {
  const response = await request.post('/api/strategy-nodes', { data: { name, definition } });
  expect(response.status()).toBe(201);

  return ((await response.json()) as { id: string }).id;
}

/** Selects a node by its id with the canvas search, which works even where nodes overlap. */
export async function selectNode(page: Page, id: string) {
  await page.getByRole('button', { name: 'Найти узел на холсте' }).click();
  await page.getByRole('searchbox', { name: 'Узел на холсте' }).fill(id);
  await page.getByRole('searchbox', { name: 'Узел на холсте' }).press('Enter');
}

/** Adds a node from the palette by its title. */
export async function addNode(page: Page, title: string) {
  await page.getByRole('button', { name: 'Добавить узел' }).click();
  await page
    .getByRole('dialog', { name: 'Библиотека узлов' })
    .getByRole('button', { name: title, exact: true })
    .click();
}

/** A definition with node ids replaced by what the nodes are, so graphs built by hand compare with references. */
export function normalized(definition: Definition) {
  const label = new Map(definition.nodes.map((node) => [node.id, node.ref ?? node.kind]));
  const pin = (reference: string) => {
    const [node = '', name] = reference.split('.');

    return `${label.get(node)}.${name}`;
  };

  return {
    direction: definition.direction,
    parameters: definition.parameters,
    nodes: definition.nodes
      .map((node) =>
        JSON.stringify({ what: label.get(node.id), settings: node.settings, inputs: node.inputs }),
      )
      .sort(),
    wires: (definition.wires ?? []).map((wire) => `${pin(wire.from)}->${pin(wire.to)}`).sort(),
  };
}
