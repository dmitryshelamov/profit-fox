import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import { toFlow } from './flow';
import type { StrategyDef } from './types';

const references = { ema } as unknown as Record<string, StrategyDef>;

describe('definition → canvas', () => {
  it('gives every node a canvas node and every wire an edge between its pins', () => {
    const definition = references.ema!;

    const { nodes, edges } = toFlow(definition);

    expect(nodes.map((node) => node.id)).toEqual(definition.nodes.map((node) => node.id));
    expect(edges).toHaveLength(definition.wires!.length);
    expect(edges[0]).toMatchObject({
      source: 'candle',
      sourceHandle: 'close',
      target: 'cross',
      targetHandle: 'source',
    });
  });

  it('keeps saved positions and places the other nodes without saving their positions', () => {
    const { nodes } = toFlow(references.ema!);

    expect(nodes.find((node) => node.id === 'cross')).toMatchObject({
      position: { x: 240, y: 0 },
      data: { placed: true },
    });
    expect(nodes.find((node) => node.id === 'candle')!.data.placed).toBe(false);
  });
});
