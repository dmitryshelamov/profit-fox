import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import { compareDrafts, sameEdit } from './compare';
import { addNode, connect, disconnect, moveNode } from './operations';
import type { StrategyDef } from './types';

const saved = { name: 'EMA', definition: ema as unknown as StrategyDef };

describe('draft against the saved definition', () => {
  it('sees no difference when only positions moved', () => {
    const draft = { ...saved, definition: moveNode(saved.definition, 'candle', { x: 1, y: 1 }) };

    expect(compareDrafts(saved, draft)).toEqual({
      same: true,
      nodesAdded: 0,
      nodesRemoved: 0,
      wiresAdded: 0,
      wiresRemoved: 0,
    });
  });

  it('counts nodes and wires added and removed', () => {
    let definition = addNode(saved.definition, [], { kind: 'ema' }, { x: 0, y: 0 }).definition;
    definition = addNode(definition, [], { kind: 'ema' }, { x: 0, y: 0 }).definition;
    definition = disconnect(definition, 'entry.result', 'enter.when');
    definition = connect(definition, 'candle.close', 'ema1.source');

    expect(compareDrafts(saved, { ...saved, definition })).toEqual({
      same: false,
      nodesAdded: 2,
      nodesRemoved: 0,
      wiresAdded: 1,
      wiresRemoved: 1,
    });
  });

  it('sees a renamed strategy as a difference', () => {
    expect(compareDrafts(saved, { ...saved, name: 'EMA 2' }).same).toBe(false);
  });

  it('counts a moved node as an edit to save', () => {
    const moved = { ...saved, definition: moveNode(saved.definition, 'candle', { x: 1, y: 1 }) };

    expect(sameEdit(saved, moved)).toBe(false);
    expect(sameEdit(saved, { ...saved })).toBe(true);
  });
});
