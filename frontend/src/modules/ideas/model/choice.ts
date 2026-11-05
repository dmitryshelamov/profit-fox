import { setupChoices, setupIntervals } from '@/modules/market';
import { oneOf, readStoredObject, writeStored } from '@/shared/storage/storedChoice';

import { type IdeaFilters, noIdeaFilters } from '../api';
import { ideaTypes } from './labels';
import { type IdeaSortColumn, ideaSortColumns, type SortDirection } from './sort';

const key = 'profit-fox.ideas.choice';

/** What the owner chose on the screen of the ideas: the filters, the saved filter and the sorting. */
export interface IdeasChoice {
  readonly filters: IdeaFilters;
  readonly savedFilterId: string | null;
  readonly sort: { readonly column: IdeaSortColumn; readonly direction: SortDirection };
}

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};

const allowed = (values: readonly { value: string }[]) => ['', ...values.map((item) => item.value)];

const defaultSort = { column: 'expectancy', direction: 'desc' } as const;

/** A direction means something only with its column: a stale column takes the default sorting whole. */
function sortOf(stored: Record<string, unknown>): IdeasChoice['sort'] {
  return ideaSortColumns.some((column) => column === stored.column)
    ? {
        column: oneOf(stored.column, ideaSortColumns, defaultSort.column),
        direction: oneOf(stored.direction, ['asc', 'desc'], defaultSort.direction),
      }
    : defaultSort;
}

/** The choice kept in the browser; a field that is missing, broken or no longer known is as it is when nothing is chosen. */
export function loadIdeasChoice(): IdeasChoice {
  const stored = readStoredObject(key) ?? {};
  const filters = record(stored.filters);
  const sort = record(stored.sort);

  return {
    filters: {
      setup: oneOf(filters.setup, allowed(setupChoices), noIdeaFilters.setup),
      interval: oneOf(filters.interval, allowed(setupIntervals), noIdeaFilters.interval),
      type: oneOf(filters.type, allowed(ideaTypes), noIdeaFilters.type),
      side: oneOf(filters.side, ['', 'long', 'short'], noIdeaFilters.side),
      category: oneOf(filters.category, ['', 'linear', 'spot'], noIdeaFilters.category),
      nonNegativeExpectancy: filters.nonNegativeExpectancy === true,
    },
    savedFilterId: typeof stored.savedFilterId === 'string' ? stored.savedFilterId : null,
    sort: sortOf(sort),
  };
}

export function saveIdeasChoice(choice: IdeasChoice): void {
  writeStored(key, choice);
}
