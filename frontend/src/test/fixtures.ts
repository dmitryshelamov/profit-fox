export const emaSummary = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'EMA',
  direction: 'long',
  supportedCategories: ['spot', 'linear'],
};

export const ladderSummary = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Ladder',
  direction: 'long',
  supportedCategories: ['spot', 'linear'],
};

export const emaShortSummary = {
  id: '33333333-3333-4333-8333-333333333333',
  name: 'EMA Short',
  direction: 'short',
  supportedCategories: ['linear'],
};

export const trendFilter = {
  id: '44444444-4444-4444-8444-444444444444',
  title: 'Фильтр тренда',
  group: 'custom',
  inputs: [{ name: 'source', type: 'number' }],
  outputs: [{ name: 'aboveTrend', type: 'boolean' }],
  settings: [],
  definition: null,
};

export function savedStrategy(
  summary: { id: string; name: string; direction: string },
  definition: unknown,
) {
  return {
    ...summary,
    supportedCategories: summary.direction === 'short' ? ['linear'] : ['spot', 'linear'],
    definition,
    version: 1,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T10:00:00Z',
  };
}

export const trendFilterDefinition = {
  formatVersion: 1,
  inputs: [{ name: 'source', type: 'number' }],
  outputs: [{ name: 'aboveTrend', type: 'boolean' }],
  nodes: [
    { id: 'in', kind: 'inputs' },
    { id: 'ema', kind: 'ema', inputs: { period: 200 } },
    { id: 'above', kind: 'compare', settings: { operation: 'gt' } },
    { id: 'out', kind: 'outputs' },
  ],
  wires: [
    { from: 'in.source', to: 'ema.source' },
    { from: 'in.source', to: 'above.a' },
    { from: 'ema.value', to: 'above.b' },
    { from: 'above.result', to: 'out.aboveTrend' },
  ],
  layout: { in: { x: 0, y: 0 }, out: { x: 600, y: 0 } },
};
