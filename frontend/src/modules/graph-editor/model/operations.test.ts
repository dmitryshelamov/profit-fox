import catalogJson from '@/test/catalog.fixture.json';

import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import {
  addNode,
  connect,
  disconnect,
  moveNode,
  moveNodes,
  removeNode,
  renamePin,
  setDeclarations,
  setInputValue,
  setParameters,
  setSetting,
} from './operations';
import type { CatalogNode, CompositeDef, StrategyDef } from './types';

const catalog = catalogJson as CatalogNode[];
const strategy = ema as unknown as StrategyDef;
const wires = (definition: { wires?: readonly { from: string; to: string }[] }) =>
  (definition.wires ?? []).map((wire) => `${wire.from}->${wire.to}`);

describe('graph operations', () => {
  it('adds a node with a free id, the default settings of the catalog and its position', () => {
    const { definition, id } = addNode(strategy, catalog, { kind: 'market-order' }, { x: 5, y: 6 });

    expect(id).toBe('market-order1');
    expect(definition.nodes.at(-1)).toEqual({
      id: 'market-order1',
      kind: 'market-order',
      settings: { side: 'buy', size: 'quantity' },
    });
    expect(definition.layout?.['market-order1']).toEqual({ x: 5, y: 6 });
    expect(addNode(definition, catalog, { kind: 'market-order' }, { x: 0, y: 0 }).id).toBe(
      'market-order2',
    );
  });

  it('adds a composite node by its reference', () => {
    const { definition, id } = addNode(
      strategy,
      catalog,
      { kind: 'composite', ref: 'ladder' },
      { x: 0, y: 0 },
    );

    expect(id).toBe('ladder1');
    expect(definition.nodes.at(-1)).toEqual({ id: 'ladder1', kind: 'composite', ref: 'ladder' });
    const custom = addNode(
      strategy,
      catalog,
      { kind: 'composite', ref: '7a1f0c1e-0000-4000-8000-000000000001' },
      { x: 0, y: 0 },
    );
    expect(custom.id).toBe('node1');
  });

  it('moves a node by saving its position', () => {
    expect(moveNode(strategy, 'candle', { x: 1, y: 2 }).layout?.candle).toEqual({ x: 1, y: 2 });
  });

  it('moves several nodes in one edit', () => {
    const moved = moveNodes(strategy, { candle: { x: 1, y: 2 }, flat: { x: 3, y: 4 } });

    expect(moved.layout).toEqual({
      ...strategy.layout,
      candle: { x: 1, y: 2 },
      flat: { x: 3, y: 4 },
    });
  });

  it('removes a node with its wires and position', () => {
    const removed = removeNode(strategy, 'cross');

    expect(removed.nodes.map((node) => node.id)).not.toContain('cross');
    expect(wires(removed).some((wire) => wire.includes('cross.'))).toBe(false);
    expect(removed.layout?.cross).toBeUndefined();
  });

  it('connects an input, replacing its wire and its value', () => {
    const valued = setInputValue(strategy, 'enter', 'size', 5);
    const connected = connect(valued, 'candle.close', 'enter.size');

    expect(wires(connected)).toContain('candle.close->enter.size');
    expect(wires(connected)).not.toContain('params.amount->enter.size');
    expect(connected.nodes.find((node) => node.id === 'enter')?.inputs).toBeUndefined();
  });

  it('disconnects a wire', () => {
    expect(wires(disconnect(strategy, 'entry.result', 'enter.when'))).not.toContain(
      'entry.result->enter.when',
    );
  });

  it('sets and clears the value of an input', () => {
    const set = setInputValue(strategy, 'flat', 'value', true);

    expect(set.nodes.find((node) => node.id === 'flat')?.inputs).toEqual({ value: true });
    expect(
      setInputValue(set, 'flat', 'value', undefined).nodes.find((node) => node.id === 'flat')
        ?.inputs,
    ).toBeUndefined();
  });

  it('changes a setting and drops wires its pins no longer accept', () => {
    const withCompare = connect(
      addNode(strategy, catalog, { kind: 'compare' }, { x: 0, y: 0 }).definition,
      'candle.close',
      'compare1.a',
    );

    const changed = setSetting(withCompare, catalog, 'compare1', 'type', 'duration');

    expect(changed.nodes.find((node) => node.id === 'compare1')?.settings).toEqual({
      operation: 'gt',
      type: 'duration',
    });
    expect(wires(changed)).not.toContain('candle.close->compare1.a');
  });

  it('changes parameters and drops wires from parameters that are gone', () => {
    const changed = setParameters(
      strategy,
      catalog,
      strategy.parameters!.filter((parameter) => parameter.name !== 'amount'),
    );

    expect(changed.parameters?.map((parameter) => parameter.name)).toEqual(['fast', 'slow']);
    expect(wires(changed)).not.toContain('params.amount->enter.size');
    expect(wires(changed)).toContain('params.fast->cross.fast');
  });

  it('renames a parameter together with its wires', () => {
    const renamed = renamePin(strategy, catalog, 'parameters', 'fast', 'quick');

    expect(renamed.parameters?.map((parameter) => parameter.name)).toEqual([
      'quick',
      'slow',
      'amount',
    ]);
    expect(wires(renamed)).toContain('params.quick->cross.fast');
  });

  it('changes the declarations of a composite node and drops wires to pins that are gone', () => {
    const node: CompositeDef = {
      formatVersion: 1,
      inputs: [{ name: 'source', type: 'number' }],
      outputs: [{ name: 'on', type: 'boolean' }],
      nodes: [
        { id: 'in', kind: 'inputs' },
        { id: 'above', kind: 'compare', settings: { operation: 'gt' }, inputs: { b: 1 } },
        { id: 'out', kind: 'outputs' },
      ],
      wires: [
        { from: 'in.source', to: 'above.a' },
        { from: 'above.result', to: 'out.on' },
      ],
    };

    const changed = setDeclarations(node, catalog, { inputs: node.inputs, outputs: [] });
    const renamed = renamePin(node, catalog, 'inputs', 'source', 'price');

    expect(wires(changed)).toEqual(['in.source->above.a']);
    expect(renamed.inputs).toEqual([{ name: 'price', type: 'number' }]);
    expect(wires(renamed)).toContain('in.price->above.a');
  });
});
