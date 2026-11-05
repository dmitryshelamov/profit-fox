import { ideaOf } from '@/test/fixtures';

import { type IdeaSortColumn, sortIdeas } from './sort';

type Idea = ReturnType<typeof ideaOf>;

const bucketOf = (overrides: Partial<NonNullable<Idea['bucket']>>) => ({
  ...ideaOf().bucket!,
  ...overrides,
});
const ids = (ideas: readonly Idea[]) => ideas.map((idea) => idea.id);

describe('sortIdeas', () => {
  // Each row: the column, three ideas listed in the order of the API, and their order by the column ascending.
  it.each<[IdeaSortColumn, Partial<Idea>[], string[]]>([
    [
      'firedAt',
      [
        { firedAt: '2026-11-04T11:45:00Z' },
        { firedAt: '2026-11-03T09:00:00Z' },
        { firedAt: '2026-11-04T08:00:00Z' },
      ],
      ['b', 'c', 'a'],
    ],
    [
      'symbol',
      [{ symbol: 'WIFUSDT' }, { symbol: 'ARMUSDT' }, { symbol: 'SOLUSDT' }],
      ['b', 'c', 'a'],
    ],
    [
      'setup',
      [{ setup: 'early_impulse' }, { setup: 'impulse' }, { setup: 'drop' }],
      ['b', 'c', 'a'],
    ],
    ['interval', [{ interval: '240' }, { interval: '5' }, { interval: '60' }], ['b', 'c', 'a']],
    [
      'type',
      [{ type: 'pullback' }, { type: 'beyond_extreme' }, { type: 'levels' }],
      ['b', 'a', 'c'],
    ],
    ['entry', [{ entry: '184.7' }, { entry: '9.5' }, { entry: '197.00' }], ['b', 'a', 'c']],
    ['stop', [{ stop: '-2' }, { stop: '0.5' }, { stop: '-10' }], ['c', 'a', 'b']],
    ['target', [{ target: '2.60' }, { target: '2.4' }, { target: '2.55' }], ['b', 'c', 'a']],
    [
      'riskReward',
      [{ riskReward: '2' }, { riskReward: '1.55' }, { riskReward: '10' }],
      ['b', 'a', 'c'],
    ],
    [
      'probability',
      [
        { bucket: bucketOf({ probability: '0.46' }) },
        { bucket: bucketOf({ probability: '0.5' }) },
        { bucket: bucketOf({ probability: '0.405' }) },
      ],
      ['c', 'a', 'b'],
    ],
    [
      'expectancy',
      [
        { bucket: bucketOf({ expectancy: '0.38' }) },
        { bucket: bucketOf({ expectancy: '-0.05' }) },
        { bucket: bucketOf({ expectancy: '1.2' }) },
      ],
      ['b', 'a', 'c'],
    ],
    ['profit', [{ riskReward: '2' }, { riskReward: '1.5' }, { riskReward: '3' }], ['b', 'a', 'c']],
    [
      'status',
      [{ status: 'stop' }, { status: 'awaiting_entry' }, { status: 'active' }],
      ['b', 'c', 'a'],
    ],
  ])('sorts by %s', (column, overrides, ascending) => {
    const ideas = overrides.map((override, index) => ideaOf({ id: 'abc'[index]!, ...override }));

    expect(ids(sortIdeas(ideas, column, 'asc'))).toEqual(ascending);
    expect(ids(sortIdeas(ideas, column, 'desc'))).toEqual([...ascending].reverse());
  });

  it('sorts by the side, the long first, keeping the order of the API among equals', () => {
    const ideas = [
      ideaOf({ id: 'a', side: 'short' }),
      ideaOf({ id: 'b', side: 'long' }),
      ideaOf({ id: 'c', side: 'short' }),
    ];

    expect(ids(sortIdeas(ideas, 'side', 'asc'))).toEqual(['b', 'a', 'c']);
    expect(ids(sortIdeas(ideas, 'side', 'desc'))).toEqual(['a', 'c', 'b']);
  });

  it('orders the status by the course of an idea', () => {
    const ideas = ['expired', 'stop', 'target', 'active', 'awaiting_entry'].map((status) =>
      ideaOf({ id: status, status }),
    );

    expect(ids(sortIdeas(ideas, 'status', 'asc'))).toEqual([
      'awaiting_entry',
      'active',
      'target',
      'stop',
      'expired',
    ]);
  });

  // Scenario "Вероятность без данных".
  it.each(['asc', 'desc'] as const)(
    'puts the ideas without a probability under the others, %s',
    (direction) => {
      const ideas = [
        ideaOf({ id: 'few', bucket: bucketOf({ enoughData: false, probability: '0.9' }) }),
        ideaOf({ id: 'some', bucket: bucketOf({ probability: '0.46' }) }),
        ideaOf({ id: 'none', bucket: null }),
        ideaOf({ id: 'more', bucket: bucketOf({ probability: '0.51' }) }),
      ];

      const sorted = ids(sortIdeas(ideas, 'probability', direction));

      expect(sorted.slice(0, 2)).toEqual(direction === 'asc' ? ['some', 'more'] : ['more', 'some']);
      expect(sorted.slice(2)).toEqual(['few', 'none']);
    },
  );

  it.each(['asc', 'desc'] as const)(
    'puts the ideas without a bucket under the others by the expectancy, %s',
    (direction) => {
      const ideas = [
        ideaOf({ id: 'none', bucket: null }),
        ideaOf({ id: 'low', bucket: bucketOf({ expectancy: '-0.2' }) }),
        ideaOf({ id: 'high', bucket: bucketOf({ expectancy: '0.4' }) }),
      ];

      expect(ids(sortIdeas(ideas, 'expectancy', direction)).at(-1)).toBe('none');
    },
  );

  it.each(['asc', 'desc'] as const)(
    'keeps the order of the API among equal values, %s',
    (direction) => {
      const ideas = ['x', 'y', 'z'].map((id) => ideaOf({ id, riskReward: '2' }));

      expect(ids(sortIdeas(ideas, 'riskReward', direction))).toEqual(['x', 'y', 'z']);
    },
  );

  it('does not change the list it is given', () => {
    const ideas = [ideaOf({ id: 'a', riskReward: '3' }), ideaOf({ id: 'b', riskReward: '1.5' })];

    sortIdeas(ideas, 'riskReward', 'asc');

    expect(ids(ideas)).toEqual(['a', 'b']);
  });
});
