import { fundingHigh, fundingHighId, strategyCatalog, usesOf } from '@/test/strategies';

import { pinsOf } from './pins';
import {
  addSignalNode,
  mergeUses,
  strategyOfSignal,
  updateSignalVersion,
  versionKey,
  withSignalVersions,
} from './signals';
import type { GraphDocument } from './types';

const kinds = strategyCatalog;

function strategyWith(version: number, inputs?: Record<string, number>): GraphDocument {
  return {
    graph: {
      formatVersion: 1,
      parameters: [],
      outputs: [],
      nodes: [
        {
          id: 's',
          kind: 'signal',
          settings: { signal: fundingHighId, version: String(version) },
          ...(inputs && { inputs }),
        },
        { id: 'enter', kind: 'enter', settings: { side: 'short' } },
      ],
      wires: [{ from: 's.event', to: 'enter.when' }],
    },
    layout: {},
  };
}

describe('a signal node', () => {
  // Scenario «Добавление сигнала».
  it('points at the latest version of the signal and takes its pins from that version', () => {
    const empty: GraphDocument = {
      graph: { ...strategyWith(1).graph, nodes: [], wires: [] },
      layout: {},
    };

    const added = addSignalNode(
      empty,
      kinds,
      { id: fundingHighId, name: 'funding-high', version: 2 },
      { x: 0, y: 0 },
    );

    const node = added.document.graph.nodes.find((candidate) => candidate.id === added.id)!;
    expect(added.id).toBe('funding-high');
    expect(node).toEqual({
      id: 'funding-high',
      kind: 'signal',
      settings: { signal: fundingHighId, version: '2' },
    });
    const catalog = withSignalVersions(
      kinds,
      mergeUses(new Map(), usesOf(added.document.graph.nodes, [fundingHigh])),
    );
    const pins = pinsOf(node, catalog, added.document.graph);
    expect(pins.inputs.map(({ name, type }) => ({ name, type }))).toEqual([
      { name: 'threshold', type: 'number' },
    ]);
    expect(pins.outputs).toEqual([{ name: 'event', type: 'event' }]);
  });

  // Scenario «Значение по умолчанию».
  it('gives an input without a wire and a value the value of the parameter', () => {
    const document = strategyWith(1);
    const catalog = withSignalVersions(
      kinds,
      mergeUses(new Map(), usesOf(document.graph.nodes, [fundingHigh])),
    );

    expect(pinsOf(document.graph.nodes[0]!, catalog, document.graph).inputs).toEqual([
      { name: 'threshold', type: 'number', fallback: 0.0005 },
    ]);
  });

  it('has no inputs while the version is not known yet, and an event', () => {
    const document = strategyWith(1);

    expect(pinsOf(document.graph.nodes[0]!, kinds, document.graph)).toEqual({
      inputs: [],
      outputs: [{ name: 'event', type: 'event' }],
    });
  });

  // Scenario «Параметр убран в новой версии».
  it('loses the value and the wire of an input the new version has not, and keeps the others', () => {
    const document = strategyWith(1, { threshold: 0.002, limit: 3 });
    const withWire: GraphDocument = {
      ...document,
      graph: {
        ...document.graph,
        parameters: [{ name: 'cap', type: 'number', value: 1 }],
        nodes: [...document.graph.nodes, { id: 'p', kind: 'parameters' }],
        wires: [...document.graph.wires, { from: 'p.cap', to: 's.cap' }],
      },
    };

    const updated = updateSignalVersion(withWire, 's', 2, ['threshold']);

    expect(updated.graph.nodes[0]).toEqual({
      id: 's',
      kind: 'signal',
      settings: { signal: fundingHighId, version: '2' },
      inputs: { threshold: 0.002 },
    });
    expect(updated.graph.wires).toEqual([{ from: 's.event', to: 'enter.when' }]);
    expect(withWire.graph.nodes[0]!.settings!.version).toBe('1');
  });

  it('knows a version by the answers of the API, with the name of now', () => {
    const versions = mergeUses(new Map(), usesOf(strategyWith(1).graph.nodes, [fundingHigh]));

    expect(versions.get(versionKey(fundingHighId, 1))).toEqual({
      name: 'funding-high',
      latestVersion: 2,
      parameters: [
        { name: 'threshold', type: 'number', value: 0.0005, minimum: null, maximum: null },
      ],
    });
  });
});

describe('the strategy of a signal', () => {
  // Scenario «Стратегия из сигнала funding-high».
  it('is the node of the signal, an enter on its event and a close-after, without a side and a time', () => {
    expect(strategyOfSignal({ id: fundingHighId, name: 'funding-high', version: 2 }).graph).toEqual(
      {
        formatVersion: 1,
        parameters: [],
        outputs: [],
        nodes: [
          { id: 'funding-high', kind: 'signal', settings: { signal: fundingHighId, version: '2' } },
          { id: 'enter', kind: 'enter' },
          { id: 'close-after', kind: 'close-after' },
        ],
        wires: [{ from: 'funding-high.event', to: 'enter.when' }],
      },
    );
  });
});
