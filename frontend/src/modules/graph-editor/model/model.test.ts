import { catalog, h0001Graph } from '@/test/fixtures';
import { fundingShort, strategyCatalog } from '@/test/strategies';

import { canonical, differences, sameDocument } from './compare';
import { toFlow, wireOf } from './flow';
import { asGraph } from './graph';
import { issuesOf } from './issues';
import { layoutGraph, nodeWidth, signalNodeWidth, widthOf } from './layout';
import {
  addNode,
  connect,
  moveNodes,
  newId,
  removeNode,
  renameNode,
  setInputValue,
  setParameters,
} from './operations';
import type { Graph, GraphDocument } from './types';
import { parseValue } from './values';
import { canConnect } from './wires';

const h0001 = (): GraphDocument => ({ graph: h0001Graph(), layout: {} });
const compare = catalog.find((kind) => kind.kind === 'compare')!;

describe('the graph on the canvas', () => {
  it('goes to xyflow and back without losing anything', () => {
    const document = h0001();
    const flow = toFlow(document, catalog);

    expect(flow.nodes.map((node) => node.data.node)).toEqual(document.graph.nodes);
    expect(flow.edges.map(wireOf)).toEqual(document.graph.wires);
    expect(flow.nodes.map((node) => node.id)).toEqual(['p', 'rate', 'high', 'first', 'out']);
  });

  // Scenario «Граф без расположения».
  it('places a graph without layout in columns by depth, the same every time', () => {
    const places = layoutGraph(h0001Graph(), catalog);

    expect(places.p!.x).toBe(0);
    expect(places.rate!.x).toBe(0);
    expect(places.rate!.y).toBeGreaterThan(places.p!.y);
    expect(places.high!.x).toBeGreaterThan(nodeWidth);
    const last = Math.max(...Object.values(places).map((place) => place.x));
    expect(places.out!.x).toBe(last);
    expect(layoutGraph(h0001Graph(), catalog)).toEqual(places);
  });

  // A signal node is wider: its header holds the name of the signal, the badge of its version and its id.
  it('gives the column of a signal node its width, so the next column keeps its distance', () => {
    const graph = fundingShort() as unknown as Graph;
    const places = layoutGraph(graph, strategyCatalog);

    expect(widthOf(graph.nodes[0]!)).toBe(signalNodeWidth);
    expect(widthOf(graph.nodes[1]!)).toBe(nodeWidth);
    expect(signalNodeWidth).toBeGreaterThan(nodeWidth);
    // `enter` stands one column to the right of the signal; `close-after`, without wires, under the signal.
    expect(places.enter!.x - places.s!.x).toBe(signalNodeWidth + 80);
    expect(places['close-after']!.x).toBe(places.s!.x);
  });

  it('does not loop on a cycle of an invalid graph', () => {
    const graph = h0001Graph();
    const cyclic = { ...graph, wires: [...graph.wires, { from: 'first.event', to: 'rate.value' }] };

    expect(() => layoutGraph(cyclic, catalog)).not.toThrow();
  });
});

describe('the edits', () => {
  it('gives a new node a free id from its kind and its default setting', () => {
    const { document, id } = addNode(h0001(), catalog, compare, { x: 10, y: 20 });

    expect(id).toBe('compare-1');
    expect(document.graph.nodes.at(-2)).toEqual({
      id: 'compare-1',
      kind: 'compare',
      settings: { operation: 'gt' },
    });
    expect(document.graph.nodes.at(-1)!.kind).toBe('outputs');
    expect(document.layout['compare-1']).toEqual({ x: 10, y: 20 });
    expect(newId(document, 'compare')).toBe('compare-2');
  });

  it('puts a node added on a taken place down and right of it', () => {
    const first = addNode(h0001(), catalog, compare, { x: 10, y: 20 });
    const second = addNode(first.document, catalog, compare, { x: 10, y: 20 });

    expect(second.document.layout['compare-2']).toEqual({ x: 50, y: 60 });
  });

  // Scenario «H0001 с нуля»: built node by node on a new signal, the graph is the one of the file, in its order.
  it('builds the graph of H0001 from a new signal in the order of its file', () => {
    const kind = (name: string) => catalog.find((candidate) => candidate.kind === name)!;
    let document: GraphDocument = {
      graph: {
        formatVersion: 1,
        parameters: [],
        outputs: [{ name: 'entry', type: 'event' }],
        nodes: [{ id: 'out', kind: 'outputs' }],
        wires: [],
      },
      layout: {},
    };
    document = setParameters(document, [{ name: 'threshold', type: 'number', value: 0.0005 }]);
    for (const [name, id] of [
      ['parameters', 'p'],
      ['funding-rate', 'rate'],
      ['compare', 'high'],
      ['first-time', 'first'],
    ] as const) {
      const added = addNode(document, catalog, kind(name), { x: 0, y: 0 });
      document = renameNode(added.document, catalog, added.id, id);
    }

    document = {
      ...document,
      graph: {
        ...document.graph,
        nodes: document.graph.nodes.map((node) =>
          node.id === 'high' ? { ...node, settings: { operation: 'ge' } } : node,
        ),
      },
    };
    document = connect(document, 'rate.value', 'high.a');
    document = connect(document, 'p.threshold', 'high.b');
    document = connect(document, 'high.result', 'first.value');
    document = connect(document, 'first.event', 'out.entry');

    expect(JSON.parse(JSON.stringify(document.graph))).toEqual(h0001Graph());
  });

  it('pins the places of every node at the first edit', () => {
    const before = layoutGraph(h0001Graph(), catalog);
    const { document } = addNode(h0001(), catalog, compare, { x: 900, y: 900 });

    expect(document.layout.rate).toEqual(before.rate);
    expect(Object.keys(document.layout)).toHaveLength(6);
  });

  // Scenario «Переименование узла».
  it('moves the wires and the place of a renamed node', () => {
    const { document } = addNode(
      {
        graph: { ...h0001Graph(), nodes: h0001Graph().nodes.filter((node) => node.id !== 'high') },
        layout: {},
      },
      catalog,
      compare,
      { x: 905, y: 905 },
    );
    const wired = connect(document, 'rate.value', 'compare-1.a');

    const renamed = renameNode(wired, catalog, 'compare-1', 'high');

    expect(renamed.graph.wires).toContainEqual({ from: 'rate.value', to: 'high.a' });
    expect(renamed.graph.wires.some((wire) => wire.to.startsWith('compare-1'))).toBe(false);
    expect(renamed.layout.high).toEqual({ x: 905, y: 905 });
    expect(renamed.layout['compare-1']).toBeUndefined();
  });

  it('refuses an id that is taken', () => {
    const document = h0001();

    expect(renameNode(document, catalog, 'high', 'rate')).toBe(document);
  });

  // Scenario «Порог на пине».
  it('writes a threshold typed on a pin exactly', () => {
    const document = h0001();
    const unwired = {
      ...document,
      graph: {
        ...document.graph,
        wires: document.graph.wires.filter((wire) => wire.to !== 'high.b'),
      },
    };

    const written = setInputValue(unwired, 'high', 'b', parseValue('0.0005', 'number'));

    expect(written.graph.nodes.find((node) => node.id === 'high')!.inputs).toEqual({ b: 0.0005 });
    expect(JSON.stringify(written.graph)).toContain('"b":0.0005');
    expect(parseValue('0,001', 'number')).toBe(0.001);
    expect(parseValue('', 'number')).toBeUndefined();
  });

  it('replaces the wire on a wired input and takes its value away', () => {
    const valued = setInputValue(h0001(), 'high', 'a', 1);

    const rewired = connect(valued, 'p.threshold', 'high.a');

    expect(rewired.graph.wires.filter((wire) => wire.to === 'high.a')).toEqual([
      { from: 'p.threshold', to: 'high.a' },
    ]);
    expect(rewired.graph.nodes.find((node) => node.id === 'high')!.inputs).toBeUndefined();
  });

  it('removes a node with its wires', () => {
    const removed = removeNode(h0001(), catalog, 'high');

    expect(removed.graph.nodes.map((node) => node.id)).toEqual(['p', 'rate', 'first', 'out']);
    expect(removed.graph.wires).toEqual([{ from: 'first.event', to: 'out.entry' }]);
    expect(removed.layout.high).toBeUndefined();
  });

  it('moves the wires of a renamed parameter and drops those of a removed one', () => {
    const renamed = setParameters(h0001(), [{ name: 'level', type: 'number', value: 0.0005 }], {
      threshold: 'level',
    });

    expect(renamed.graph.wires).toContainEqual({ from: 'p.level', to: 'high.b' });
    expect(setParameters(h0001(), []).graph.wires.some((wire) => wire.from.startsWith('p.'))).toBe(
      false,
    );
  });

  it('keeps moved places', () => {
    const moved = moveNodes(h0001(), catalog, { rate: { x: 40.4, y: 153.0152 } });

    expect(moved.layout.rate).toEqual({ x: 40, y: 153 });
    expect(moved.graph).toEqual(h0001Graph());
  });
});

describe('wires', () => {
  // Scenario «Провод разных типов».
  it('go only between pins of one type on different nodes', () => {
    const graph = h0001Graph();

    expect(canConnect(graph, catalog, 'high.result', 'high.a')).toBe(false);
    expect(canConnect(graph, catalog, 'first.event', 'high.a')).toBe(false);
    expect(canConnect(graph, catalog, 'high.result', 'first.value')).toBe(true);
    expect(canConnect(graph, catalog, 'rate.value', 'high.b')).toBe(true);
    expect(canConnect(graph, catalog, 'rate.nothing', 'high.b')).toBe(false);
  });
});

describe('errors of the check', () => {
  it('go to their place: a wire marks its input, a cycle each of its nodes', () => {
    const issues = issuesOf([
      { code: 'UnknownNode', reason: 'no hgh', wire: { from: 'rate.value', to: 'hgh.a' } },
      { code: 'MissingInput', reason: 'no b', node: 'high', pin: 'b' },
      { code: 'ParameterOutOfBounds', reason: 'below', parameter: 'threshold' },
      { code: 'Cycle', reason: 'loop', nodes: ['a', 'b'] },
    ]);

    expect(issues.map((issue) => [issue.node, issue.pin, issue.parameter])).toEqual([
      ['hgh', 'a', undefined],
      ['high', 'b', undefined],
      [undefined, undefined, 'threshold'],
      ['a', undefined, undefined],
      ['b', undefined, undefined],
    ]);
    expect(issues[0]!.place).toBe('провод rate.value → hgh.a');
  });
});

describe('a draft against the file', () => {
  it('is the same whatever the order of keys', () => {
    const graph = h0001Graph();
    const reordered = JSON.parse(canonical(graph));

    expect(
      sameDocument(
        { graph, layout: { p: { x: 1, y: 2 } } },
        { graph: reordered, layout: { p: { y: 2, x: 1 } } },
      ),
    ).toBe(true);
    expect(sameDocument({ graph, layout: {} }, { graph, layout: { p: { x: 1, y: 2 } } })).toBe(
      false,
    );
  });

  it('counts the nodes and wires that differ', () => {
    const changed = setInputValue(removeNode(h0001(), catalog, 'first'), 'high', 'b', 1);

    expect(differences(h0001(), changed)).toEqual({ nodes: 2, wires: 2 });
  });
});

describe('JSON pasted into the card', () => {
  it('reads as a graph only with the outer form of the format', () => {
    expect(asGraph(h0001Graph())).toEqual(h0001Graph());
    expect(asGraph({ formatVersion: 2, nodes: [] })).toBeNull();
    expect(asGraph({ formatVersion: 1 })).toBeNull();
    expect(asGraph([1, 2])).toBeNull();
    expect(asGraph({ formatVersion: 1, nodes: [{ id: 'x', kind: 'candle' }] })).toEqual({
      formatVersion: 1,
      parameters: [],
      outputs: [],
      nodes: [{ id: 'x', kind: 'candle' }],
      wires: [],
    });
  });
});
