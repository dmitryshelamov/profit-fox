import { catalog } from '@/test/fixtures';

import { signalRules } from './document';

describe('the rules of a signal', () => {
  // Scenario «Новый сигнал».
  it('start with the event entry and the outputs node only', () => {
    expect(signalRules.start()).toEqual({
      formatVersion: 1,
      parameters: [],
      outputs: [{ name: 'entry', type: 'event' }],
      nodes: [{ id: 'out', kind: 'outputs' }],
      wires: [],
    });
  });

  // Scenario «Все виды в палитре».
  it('offer every kind of the catalog but outputs', () => {
    expect(signalRules.palette(catalog).map((kind) => kind.kind)).toEqual(
      catalog.map((kind) => kind.kind).filter((kind) => kind !== 'outputs'),
    );
  });

  it('never remove the outputs node', () => {
    expect(signalRules.canRemove({ id: 'out', kind: 'outputs' })).toBe(false);
    expect(signalRules.canRemove({ id: 'rate', kind: 'funding-rate' })).toBe(true);
  });
});
