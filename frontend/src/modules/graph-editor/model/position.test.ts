import { catalog } from '@/test/fixtures';
import ladderFile from '@/test/ladder.fixture.json';
import { fundingShort, strategyCatalog } from '@/test/strategies';

import { strategyRules } from '../document';
import { backArcRise, backWirePath, coloredWires } from '../ui/GraphCanvas';
import { legendTypes, pinLooks } from '../ui/looks';
import { backWires, toFlow } from './flow';
import { layoutGraph } from './layout';
import { connect } from './operations';
import { pinsOf } from './pins';
import { strategyOfSignal } from './signals';
import type { Graph, GraphDocument } from './types';

const ladder = ladderFile as unknown as Graph;

/** `funding-short` in the form of change add-position-node: the node of the position and its wires. */
function fundingShortWithPosition(): Graph {
  const graph = fundingShort() as unknown as Graph;
  return {
    ...graph,
    nodes: [
      graph.nodes[0]!,
      { id: 'enter', kind: 'enter', settings: { side: 'short' }, inputs: { size: 100 } },
      { id: 'position', kind: 'position' },
      { id: 'close-after', kind: 'close-after', inputs: { after: '4h' } },
    ],
    wires: [
      ...graph.wires,
      { from: 'enter.order', to: 'position.orders' },
      { from: 'position.position', to: 'close-after.position' },
      { from: 'close-after.order', to: 'position.orders' },
    ],
  };
}

describe('the orders of the position on the canvas', () => {
  // Scenario «Вторая заявка в позицию».
  it('adds a second wire into the orders of the position, and not a wire that is already there', () => {
    const graph = fundingShortWithPosition();
    const document: GraphDocument = {
      graph: {
        ...graph,
        wires: graph.wires.filter((wire) => wire.from !== 'close-after.order'),
      },
      layout: {},
    };
    const intoOrders = (edited: GraphDocument) =>
      edited.graph.wires.filter((wire) => wire.to === 'position.orders');
    expect(intoOrders(document)).toEqual([{ from: 'enter.order', to: 'position.orders' }]);

    const added = connect(document, 'close-after.order', 'position.orders', strategyCatalog);

    expect(intoOrders(added)).toEqual([
      { from: 'enter.order', to: 'position.orders' },
      { from: 'close-after.order', to: 'position.orders' },
    ]);
    const again = connect(added, 'close-after.order', 'position.orders', strategyCatalog);
    expect(intoOrders(again)).toHaveLength(2);
  });

  it('still replaces the wire of an input that takes one', () => {
    const document: GraphDocument = { graph: fundingShortWithPosition(), layout: {} };

    const replaced = connect(document, 'enter.order', 'close-after.position', strategyCatalog);

    expect(replaced.graph.wires.filter((wire) => wire.to === 'close-after.position')).toEqual([
      { from: 'enter.order', to: 'close-after.position' },
    ]);
  });

  // Change add-position-node, task 12.2: a wire and a value on one input is `WireAndValue`, which the canvas cannot fix.
  it('drops a value written on the orders of the position when a wire comes', () => {
    const graph = fundingShortWithPosition();
    const document: GraphDocument = {
      graph: {
        ...graph,
        nodes: graph.nodes.map((node) =>
          node.id === 'position' ? { ...node, inputs: { orders: 'x' } } : node,
        ),
        wires: graph.wires.filter((wire) => wire.to !== 'position.orders'),
      },
      layout: {},
    };

    const first = connect(document, 'enter.order', 'position.orders', strategyCatalog);

    expect(first.graph.nodes.find((node) => node.id === 'position')).toEqual({
      id: 'position',
      kind: 'position',
    });
    expect(first.graph.wires.filter((wire) => wire.to === 'position.orders')).toEqual([
      { from: 'enter.order', to: 'position.orders' },
    ]);
  });

  it('knows the input that takes several wires from the catalog', () => {
    const position = pinsOf({ id: 'position', kind: 'position' }, strategyCatalog, ladder);

    expect(position.inputs).toEqual([{ name: 'orders', type: 'order', many: true }]);
    expect(position.outputs.map((pin) => pin.type)).toEqual([
      'position',
      'boolean',
      'number',
      'number',
      'number',
      'number',
      'duration',
    ]);
  });
});

describe('the types of an order and of the position', () => {
  // Scenarios «Легенда стратегии» and «Легенда сигнала».
  it('are in the legend of a strategy and not in that of a signal', () => {
    expect(legendTypes(strategyCatalog)).toEqual([
      'number',
      'boolean',
      'duration',
      'event',
      'order',
      'position',
    ]);
    expect(legendTypes(catalog)).toEqual(['number', 'boolean', 'duration', 'event']);
  });

  it('color the wire of an order and the wire of the position by their tokens', () => {
    const document: GraphDocument = { graph: fundingShortWithPosition(), layout: {} };

    const wires = coloredWires(
      toFlow(document, strategyCatalog).edges,
      document,
      strategyCatalog,
      [],
    );

    const order = wires.find((wire) => wire.id === 'enter.order->position.orders')!;
    const position = wires.find((wire) => wire.id === 'position.position->close-after.position')!;
    expect(order.className).toContain('wire-order');
    expect(order.style?.stroke).toBe('var(--color-pin-order)');
    expect(position.style?.stroke).toBe('var(--color-pin-position)');
    expect(new Set(Object.values(pinLooks).map((look) => look.stroke)).size).toBe(6);
  });
});

describe('a wire back into the position', () => {
  // Scenario «Расстановка funding-short».
  it('is the order of a node that reads the position, and the layout goes left to right without it', () => {
    const graph = fundingShortWithPosition();

    const places = layoutGraph(graph, strategyCatalog);

    expect([...backWires(graph, strategyCatalog)]).toEqual(['close-after.order->position.orders']);
    expect(places.s!.x).toBeLessThan(places.enter!.x);
    expect(places.enter!.x).toBeLessThan(places.position!.x);
    expect(places.position!.x).toBeLessThan(places['close-after']!.x);
    const edge = coloredWires(
      toFlow({ graph, layout: {} }, strategyCatalog).edges,
      { graph, layout: {} },
      strategyCatalog,
      [],
    ).find((wire) => wire.id === 'close-after.order->position.orders')!;
    expect(edge.className).toContain('wire-back');
    expect(edge.type).toBe('back');
  });

  // Scenario «Расстановка лесенки».
  it('makes every order of the ladder a wire back and puts the position in the left column', () => {
    const places = layoutGraph(ladder, strategyCatalog);

    expect([...backWires(ladder, strategyCatalog)].sort()).toEqual(
      [
        'buy.order->position.orders',
        'entry.order->position.orders',
        'sell.order->position.orders',
      ].sort(),
    );
    expect(places.position!.x).toBe(0);
    expect(places.sell!.x).toBeGreaterThan(places['sell-price']!.x);
  });
});

/** How far the arc of a path `M x,y C x,y x,y x,y` rises above the higher of its two ends, in px. */
function riseOf(path: string): number {
  const [from, first, second, to] = [...path.matchAll(/(-?[\d.]+),(-?[\d.]+)/g)].map((point) =>
    Number(point[2]),
  ) as [number, number, number, number];
  let top = Infinity;
  for (let step = 0; step <= 1000; step += 1) {
    const t = step / 1000;
    const y =
      (1 - t) ** 3 * from +
      3 * (1 - t) ** 2 * t * first +
      3 * (1 - t) * t ** 2 * second +
      t ** 3 * to;
    top = Math.min(top, y);
  }

  return Math.min(from, to) - top;
}

// Change add-position-node, task 12.5: an arc that grew with the distance went above the canvas after «Вписать».
describe('the arc of a wire back', () => {
  it('rises 60 px over its ends when they are near', () => {
    expect(backArcRise).toBe(60);
    expect(riseOf(backWirePath(500, 100, 200, 100))).toBeCloseTo(60);
  });

  it('rises no higher when its ends are far apart', () => {
    const long = riseOf(backWirePath(2600, 100, 200, 100));

    expect(long).toBeGreaterThan(30);
    expect(long).toBeLessThanOrEqual(60);
  });

  it('rises no higher over the higher end when the other is far below', () => {
    for (const path of [backWirePath(2600, 900, 200, 100), backWirePath(900, 100, 200, 900)]) {
      expect(riseOf(path)).toBeGreaterThan(20);
      expect(riseOf(path)).toBeLessThanOrEqual(60);
    }
  });
});

describe('the graph a strategy starts with', () => {
  const wires = [
    { from: 'enter.order', to: 'position.orders' },
    { from: 'position.position', to: 'close-after.position' },
    { from: 'close-after.order', to: 'position.orders' },
  ];

  // Scenario «Новая стратегия».
  it('is an enter of 100 USDT, the position and a close-after, wired', () => {
    const graph = strategyRules.start();

    expect(graph.nodes).toEqual([
      { id: 'enter', kind: 'enter', inputs: { size: 100 } },
      { id: 'position', kind: 'position' },
      { id: 'close-after', kind: 'close-after' },
    ]);
    expect(graph.wires).toEqual(wires);
  });

  // Scenario «Стратегия из сигнала funding-high».
  it('of a signal has the node of the signal on the enter, and the position and a close-after', () => {
    const { graph } = strategyOfSignal({ id: 'id-1', name: 'funding-high', version: 2 });

    expect(graph.nodes.map((node) => node.id)).toEqual([
      'funding-high',
      'enter',
      'position',
      'close-after',
    ]);
    expect(graph.nodes[1]).toEqual({ id: 'enter', kind: 'enter', inputs: { size: 100 } });
    expect(graph.wires).toEqual([{ from: 'funding-high.event', to: 'enter.when' }, ...wires]);
  });

  // Change add-position-node, task 12.7: a signal named `position` gave two nodes with one id.
  it.each(['position', 'enter', 'close-after'])(
    'of a signal named %s gives the node of the signal a free id, and its wires go where they went',
    (name) => {
      const { graph } = strategyOfSignal({ id: 'id-1', name, version: 3 });

      expect(graph.nodes.map((node) => node.id)).toEqual([
        `${name}-1`,
        'enter',
        'position',
        'close-after',
      ]);
      expect(graph.nodes.map((node) => node.kind)).toEqual([
        'signal',
        'enter',
        'position',
        'close-after',
      ]);
      expect(graph.nodes[0]!.settings).toEqual({ signal: 'id-1', version: '3' });
      expect(graph.wires).toEqual([{ from: `${name}-1.event`, to: 'enter.when' }, ...wires]);
    },
  );
});
