import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import { issuesOf } from './issues';
import type { StrategyDef } from './types';

const strategy: StrategyDef = {
  ...(ema as unknown as StrategyDef),
  nodes: [
    ...(ema as unknown as StrategyDef).nodes,
    { id: 'cross2', kind: 'composite', ref: 'ema-cross' },
  ],
};

describe('error paths', () => {
  it('puts an error of an input on its node and pin', () => {
    expect(
      issuesOf({ 'graph.enter.when': ['Input is not connected and has no value.'] }, strategy),
    ).toEqual([
      {
        key: 'graph.enter.when',
        message: 'Input is not connected and has no value.',
        nodeId: 'enter',
        pin: 'when',
      },
    ]);
  });

  it('puts an error inside a composite node on the node the graph uses', () => {
    expect(issuesOf({ 'graph.cross2/fastEma.period': ['x'] }, strategy)).toEqual([
      { key: 'graph.cross2/fastEma.period', message: 'x', nodeId: 'cross2' },
    ]);
  });

  it('tells an error of a node from an error of a parameter by what the graph has', () => {
    expect(
      issuesOf(
        {
          'graph.cross': ['No such composite node.'],
          'graph.fast': ['Default value is out of range.'],
        },
        strategy,
      ),
    ).toEqual([
      { key: 'graph.cross', message: 'No such composite node.', nodeId: 'cross' },
      { key: 'graph.fast', message: 'Default value is out of range.', parameter: 'fast' },
    ]);
  });

  it('puts errors of reading the definition on the parameter or node at their index', () => {
    expect(
      issuesOf(
        { 'definition.parameters[1].min': ['x'], 'definition.nodes[2].kind': ['y'], name: ['z'] },
        strategy,
      ),
    ).toEqual([
      { key: 'definition.parameters[1].min', message: 'x', parameter: 'slow' },
      { key: 'definition.nodes[2].kind', message: 'y', nodeId: 'position' },
      { key: 'name', message: 'z', field: 'name' },
    ]);
  });

  it('keeps an error of the whole graph without a target', () => {
    expect(issuesOf({ graph: ['Wires form a cycle: a, b.'] }, strategy)).toEqual([
      { key: 'graph', message: 'Wires form a cycle: a, b.' },
    ]);
  });
});
