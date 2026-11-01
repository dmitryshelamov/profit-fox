import catalogJson from '@/test/catalog.fixture.json';

import dca from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/dca.json';
import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import emaShort from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema-short.json';
import ladder from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ladder.json';
import { keepPlaces, layoutGraph, nodeSize } from './layout';
import { contextOf, moveNode, removeNode } from './operations';
import type { CatalogNode, StrategyDef } from './types';

const catalog = catalogJson as CatalogNode[];
const references = { ema, 'ema-short': emaShort, dca, ladder } as unknown as Record<
  string,
  StrategyDef
>;
const nodeOf = (reference: string) => reference.slice(0, reference.indexOf('.'));

describe('layout by stages', () => {
  // Scenario "Провода идут слева направо", on every reference strategy.
  it.each(Object.keys(references))(
    'puts the source of every wire of %s left of its target',
    (name) => {
      const definition = references[name]!;

      const positions = layoutGraph(definition, catalog);

      for (const wire of definition.wires ?? []) {
        expect(positions[nodeOf(wire.from)]!.x, `${wire.from} -> ${wire.to}`).toBeLessThan(
          positions[nodeOf(wire.to)]!.x,
        );
      }
    },
  );

  // Scenario "Решения разными полосами".
  it('puts the nodes of Ladder leading to the entry above those leading only to the sell, and those above the buy', () => {
    const positions = layoutGraph(references.ladder!, catalog);
    const ys = (ids: string[]) => ids.map((id) => positions[id]!.y);

    const entry = ys(['params', 'position', 'flat', 'entry']);
    const sell = ys(['riseFactor', 'sellPrice', 'sell']);
    const buy = ys(['dropFactor', 'buyPrice', 'nextInvested', 'budget', 'canAverage', 'buy']);

    expect(Math.max(...entry)).toBeLessThan(Math.min(...sell));
    expect(Math.max(...sell)).toBeLessThan(Math.min(...buy));
  });

  it('puts the nodes that lead to no decision below the decisions', () => {
    const cyclic: StrategyDef = {
      formatVersion: 1,
      direction: 'long',
      nodes: [
        { id: 'a', kind: 'not' },
        { id: 'b', kind: 'not' },
        { id: 'alone', kind: 'position' },
      ],
      wires: [
        { from: 'a.result', to: 'b.value' },
        { from: 'b.result', to: 'a.value' },
      ],
    };

    const positions = layoutGraph(cyclic, catalog);

    expect(positions.a!.y).toBeGreaterThan(positions.alone!.y);
    expect(positions.b!.y).toBeGreaterThan(positions.alone!.y);
  });

  it('sizes a node by its header, its settings row and its rows of pins', () => {
    const context = contextOf(references.ladder!);
    const node = (id: string) => references.ladder!.nodes.find((candidate) => candidate.id === id)!;

    expect(nodeSize(node('flat'), catalog, context)).toEqual({ width: 200, height: 65 });
    expect(nodeSize(node('entry'), catalog, context)).toEqual({ width: 200, height: 112 });
    expect(nodeSize(node('params'), catalog, context)).toEqual({ width: 200, height: 131 });
  });

  it.each(Object.keys(references))('places the nodes of %s without overlaps', (name) => {
    const definition = references[name]!;
    const positions = layoutGraph(definition, catalog);
    const boxes = definition.nodes.map((node) => ({
      id: node.id,
      ...positions[node.id]!,
      ...nodeSize(node, catalog, contextOf(definition)),
    }));

    for (const [index, box] of boxes.entries()) {
      for (const other of boxes.slice(index + 1)) {
        const apart =
          box.x + box.width <= other.x ||
          other.x + other.width <= box.x ||
          box.y + box.height <= other.y ||
          other.y + other.height <= box.y;

        expect(apart, `${box.id} and ${other.id}`).toBe(true);
      }
    }
  });

  it('gives the same layout on every call', () => {
    expect(layoutGraph(references.ladder!, catalog)).toEqual(
      layoutGraph(references.ladder!, catalog),
    );
  });

  it('places the nodes of a cycle in an invalid graph without looping', () => {
    const cyclic: StrategyDef = {
      formatVersion: 1,
      direction: 'long',
      nodes: [
        { id: 'a', kind: 'not' },
        { id: 'b', kind: 'not' },
      ],
      wires: [
        { from: 'a.result', to: 'b.value' },
        { from: 'b.result', to: 'a.value' },
      ],
    };

    const positions = layoutGraph(cyclic, catalog);

    expect(Object.keys(positions)).toEqual(['a', 'b']);
  });
});

describe('keeping the places of nodes on an edit', () => {
  const ladderDef = references.ladder!;

  it('saves where every node stood before the edit, next to what the edit placed', () => {
    const staged = layoutGraph(ladderDef, catalog);

    const kept = keepPlaces(ladderDef, moveNode(ladderDef, 'flat', { x: 5, y: 6 }), catalog);

    expect(kept.layout).toEqual({ ...staged, flat: { x: 5, y: 6 } });
  });

  it('pins a node at its saved position, not where the layout by stages would put it', () => {
    const before = { ...ladderDef, layout: { flat: { x: 5, y: 6 } } };

    const kept = keepPlaces(before, ladderDef, catalog);

    expect(kept.layout!.flat).toEqual({ x: 5, y: 6 });
  });

  it('does not place nodes the edit removed', () => {
    const kept = keepPlaces(ladderDef, removeNode(ladderDef, 'sell'), catalog);

    expect(Object.keys(kept.layout!)).not.toContain('sell');
    expect(Object.keys(kept.layout!)).toHaveLength(ladderDef.nodes.length - 1);
  });

  it('leaves a node that was not there before without a place', () => {
    const after = { ...ladderDef, nodes: [...ladderDef.nodes, { id: 'extra', kind: 'not' }] };

    const kept = keepPlaces(ladderDef, after, catalog);

    expect(Object.keys(kept.layout!)).not.toContain('extra');
  });
});
