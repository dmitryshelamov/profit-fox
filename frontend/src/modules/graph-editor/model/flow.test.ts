import dca from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/dca.json';
import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import emaShort from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema-short.json';
import ladder from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ladder.json';
import { fromFlow, toFlow } from './flow';
import type { StrategyDef } from './types';

const references = { ema, 'ema-short': emaShort, dca, ladder } as unknown as Record<
  string,
  StrategyDef
>;

describe('definition ↔ canvas', () => {
  it.each(Object.keys(references))('%s goes to the canvas and back unchanged', (name) => {
    const definition = references[name]!;

    const flow = toFlow(definition);

    expect(fromFlow(definition, flow.nodes, flow.edges)).toEqual(definition);
  });

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

  it('saves the position of a node moved on the canvas', () => {
    const definition = references.ema!;
    const flow = toFlow(definition);
    const moved = flow.nodes.map((node) =>
      node.id === 'candle'
        ? { ...node, position: { x: 10, y: 20 }, data: { ...node.data, placed: true } }
        : node,
    );

    expect(fromFlow(definition, moved, flow.edges).layout).toEqual({
      ...definition.layout,
      candle: { x: 10, y: 20 },
    });
  });
});
