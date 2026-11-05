import { setupLabel } from '@/modules/market';
import type { Schemas } from '@/shared/api/client';
import { compareDecimals } from '@/shared/format/decimal';

import { sideLabel, typeLabel } from './labels';

type Idea = Schemas['IdeaResponse'];

export const ideaSortColumns = [
  'firedAt',
  'symbol',
  'setup',
  'interval',
  'type',
  'side',
  'entry',
  'stop',
  'target',
  'riskReward',
  'probability',
  'expectancy',
  'profit',
  'status',
] as const;

export type IdeaSortColumn = (typeof ideaSortColumns)[number];

export type SortDirection = 'asc' | 'desc';

const statusCourse = ['awaiting_entry', 'active', 'target', 'stop', 'expired'];

const text = (left: string, right: string) => left.localeCompare(right, 'ru');

/** The key of an idea in a column; `null` means the idea has no value there. */
const columns: Record<IdeaSortColumn, (idea: Idea) => string | null> = {
  firedAt: (idea) => idea.firedAt,
  symbol: (idea) => idea.symbol,
  setup: (idea) => setupLabel(idea.setup),
  interval: (idea) => idea.interval,
  type: (idea) => typeLabel(idea.type),
  side: (idea) => sideLabel(idea.side),
  entry: (idea) => idea.entry,
  stop: (idea) => idea.stop,
  target: (idea) => idea.target,
  riskReward: (idea) => idea.riskReward,
  probability: (idea) => (idea.bucket?.enoughData ? (idea.bucket.probability ?? null) : null),
  expectancy: (idea) => idea.bucket?.expectancy ?? null,
  // The profit is the amount, the same for every idea, times this share, so it goes in the order of the share.
  profit: (idea) => idea.profitPerUsdt,
  status: (idea) => String(statusCourse.indexOf(idea.status)),
};

/** How two keys of a column compare; decimal strings unless the column says otherwise. */
const comparers: Partial<Record<IdeaSortColumn, (left: string, right: string) => number>> = {
  firedAt: (left, right) => Date.parse(left) - Date.parse(right),
  symbol: text,
  setup: text,
  type: text,
  side: text,
};

/**
 * The ideas in the order of a column. Numbers and time go by value (decimal strings exactly), text by what the screen shows,
 * the status by the course of an idea. Ideas without a value in the column stay at the bottom in either direction, and equal
 * values keep the order they came in.
 */
export function sortIdeas(
  ideas: readonly Idea[],
  column: IdeaSortColumn,
  direction: SortDirection,
): Idea[] {
  const key = columns[column];
  const compare = comparers[column] ?? compareDecimals;
  const sign = direction === 'asc' ? 1 : -1;

  return ideas
    .map((idea, index) => ({ idea, index, value: key(idea) }))
    .sort((left, right) => {
      if (left.value === null || right.value === null) {
        return left.value === right.value ? left.index - right.index : left.value === null ? 1 : -1;
      }

      return sign * compare(left.value, right.value) || left.index - right.index;
    })
    .map((entry) => entry.idea);
}
