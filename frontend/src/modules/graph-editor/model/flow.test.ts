import catalogJson from '@/test/catalog.fixture.json';

import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import { toFlow } from './flow';
import { layoutGraph } from './layout';
import type { CatalogNode, StrategyDef } from './types';

const catalog = catalogJson as CatalogNode[];
const references = { ema } as unknown as Record<string, StrategyDef>;

describe('definition → canvas', () => {
  it('gives every node a canvas node and every wire an edge between its pins', () => {
    const definition = references.ema!;

    const { nodes, edges } = toFlow(definition, catalog);

    expect(nodes.map((node) => node.id)).toEqual(definition.nodes.map((node) => node.id));
    expect(edges).toHaveLength(definition.wires!.length);
    expect(edges[0]).toMatchObject({
      source: 'candle',
      sourceHandle: 'close',
      target: 'cross',
      targetHandle: 'source',
    });
  });

  // Scenario "Сохранённые координаты".
  it('keeps saved positions and lays out the other nodes by stages without saving their positions', () => {
    const definition = { ...references.ema!, layout: { cross: { x: 240, y: 0 } } };

    const { nodes } = toFlow(definition, catalog);

    expect(nodes.find((node) => node.id === 'cross')).toMatchObject({
      position: { x: 240, y: 0 },
      data: { placed: true },
    });
    expect(nodes.find((node) => node.id === 'candle')).toMatchObject({
      position: layoutGraph(definition, catalog).candle,
      data: { placed: false },
    });
  });
});
