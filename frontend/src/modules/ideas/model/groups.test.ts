import { groupPairIdeas } from './groups';

const idea = (id: string, status: string) => ({ id, status });

describe('groupPairIdeas', () => {
  it('puts the ideas waiting for entry and the active ones first, the closed ones into the expired group', () => {
    const groups = groupPairIdeas([
      idea('a', 'awaiting_entry'),
      idea('b', 'stop'),
      idea('c', 'active'),
      idea('d', 'expired'),
      idea('e', 'target'),
    ]);

    expect(groups.open.map((item) => item.id)).toEqual(['a', 'c']);
    expect(groups.closed.map((item) => item.id)).toEqual(['b', 'd', 'e']);
  });
});
