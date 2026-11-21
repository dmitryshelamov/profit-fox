import { catalog } from '@/test/fixtures';
import { strategyCatalog } from '@/test/strategies';

import { signalRules, strategyRules } from './document';

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

describe('the rules of a strategy', () => {
  // Scenario «Новая стратегия».
  it('start with an enter and a close-after without a side and a time', () => {
    expect(strategyRules.start()).toEqual({
      formatVersion: 1,
      parameters: [],
      outputs: [],
      nodes: [
        { id: 'enter', kind: 'enter' },
        { id: 'close-after', kind: 'close-after' },
      ],
      wires: [],
    });
  });

  it('offer the catalog of a strategy without outputs, the saved signals coming apart', () => {
    const kinds = strategyRules.palette(strategyCatalog).map((kind) => kind.kind);

    expect(kinds).toEqual(
      expect.arrayContaining(['enter', 'stop-loss', 'take-profit', 'close-after']),
    );
    expect(kinds).not.toContain('outputs');
    expect(kinds).not.toContain('signal');
  });

  it('remove any node and check the graph of a strategy', () => {
    expect(strategyRules.canRemove({ id: 'enter', kind: 'enter' })).toBe(true);
    expect(strategyRules.checkPath).toBe('/api/strategies/validate');
    expect(signalRules.checkPath).toBe('/api/signals/validate');
  });

  // Scenario «Черновики с одним именем».
  it('keep the draft of a strategy apart from that of a signal of the same name', () => {
    expect(strategyRules.draftKey({ name: 'funding-high' })).not.toBe(
      signalRules.draftKey({ name: 'funding-high' }),
    );
    expect(strategyRules.draftKey({ newId: 'x' })).not.toBe(signalRules.draftKey({ newId: 'x' }));
    expect(signalRules.draftKey({ name: 'funding-high' })).toBe(
      'profit-fox:draft:signal:funding-high',
    );
    expect(signalRules.draftKey({ newId: 'x' })).toBe('profit-fox:draft:signal-new:x');
  });
});
