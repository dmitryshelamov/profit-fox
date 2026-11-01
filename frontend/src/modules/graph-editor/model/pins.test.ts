import catalogJson from '@/test/catalog.fixture.json';

import { pinsOf } from './pins';
import type { CatalogNode } from './types';

const custom: CatalogNode = {
  id: '7a1f0c1e-0000-4000-8000-000000000001',
  title: 'Фильтр тренда',
  group: 'custom',
  inputs: [{ name: 'source', type: 'number' }],
  outputs: [{ name: 'aboveTrend', type: 'boolean' }],
  settings: [],
};
const catalog = [...(catalogJson as CatalogNode[]), custom];

describe('pins of a node', () => {
  it('takes a simple node from the catalog', () => {
    expect(pinsOf({ id: 'e', kind: 'ema' }, catalog, {})).toEqual({
      inputs: [
        { name: 'source', type: 'number' },
        { name: 'period', type: 'number' },
      ],
      outputs: [{ name: 'value', type: 'number' }],
    });
  });

  it('takes a built-in or custom composite node from the catalog by its reference', () => {
    expect(pinsOf({ id: 'c', kind: 'composite', ref: 'ema-cross' }, catalog, {}).outputs).toEqual([
      { name: 'up', type: 'boolean' },
      { name: 'down', type: 'boolean' },
    ]);
    expect(pinsOf({ id: 't', kind: 'composite', ref: custom.id }, catalog, {})).toEqual({
      inputs: [{ name: 'source', type: 'number' }],
      outputs: [{ name: 'aboveTrend', type: 'boolean' }],
    });
  });

  // Scenario "Новый параметр".
  it('gives the parameters node an output per strategy parameter', () => {
    const parameters = [
      { name: 'fast', type: 'number' as const },
      { name: 'stop', type: 'number' as const },
    ];

    expect(pinsOf({ id: 'p', kind: 'parameters' }, catalog, { parameters })).toEqual({
      inputs: [],
      outputs: parameters,
    });
  });

  it('gives the inputs and outputs nodes the declared pins of the composite node', () => {
    const inputs = [{ name: 'source', type: 'number' as const }];
    const outputs = [{ name: 'aboveTrend', type: 'boolean' as const }];

    expect(pinsOf({ id: 'in', kind: 'inputs' }, catalog, { inputs, outputs })).toEqual({
      inputs: [],
      outputs: inputs,
    });
    expect(pinsOf({ id: 'out', kind: 'outputs' }, catalog, { inputs, outputs })).toEqual({
      inputs: outputs,
      outputs: [],
    });
  });

  it('types the compared inputs by the type setting', () => {
    const duration = pinsOf(
      { id: 'c', kind: 'compare', settings: { operation: 'ge', type: 'duration' } },
      catalog,
      {},
    );
    const byDefault = pinsOf(
      { id: 'c', kind: 'compare', settings: { operation: 'ge' } },
      catalog,
      {},
    );

    expect(duration.inputs).toEqual([
      { name: 'a', type: 'duration' },
      { name: 'b', type: 'duration' },
    ]);
    expect(byDefault.inputs.map((pin) => pin.type)).toEqual(['number', 'number']);
    expect(duration.outputs).toEqual([{ name: 'result', type: 'boolean' }]);
  });

  it('gives an unknown node no pins', () => {
    expect(pinsOf({ id: 'x', kind: 'rsi' }, catalog, {})).toEqual({ inputs: [], outputs: [] });
  });
});
