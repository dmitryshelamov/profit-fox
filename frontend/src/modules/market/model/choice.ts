import { pageSizeOf } from '@/shared/paging/pageSizes';
import { oneOf, readStoredObject, writeStored } from '@/shared/storage/storedChoice';

import type { CategoryFilter } from './rows';
import { defaultSetupInterval, setupChoices, type SetupFilter, setupIntervals } from './setups';
import { type SortColumn, sortColumns, type SortDirection } from './sorting';

const key = 'profit-fox.market.choice';

/** What the owner chose on the market screen, but the search: the category, the setup chip and its interval, the saved filter, the sorting and the size of a page. */
export interface MarketChoice {
  readonly category: CategoryFilter;
  readonly setup: SetupFilter;
  readonly setupInterval: string;
  readonly savedFilterId: string | null;
  readonly sort: { readonly column: SortColumn; readonly direction: SortDirection };
  readonly pageSize: number;
}

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};

const defaultSort = { column: 'change1h', direction: 'desc' } as const;

/** A direction means something only with its column: a stale column takes the default sorting whole. */
function sortOf(stored: Record<string, unknown>): MarketChoice['sort'] {
  return sortColumns.some((column) => column === stored.column)
    ? {
        column: oneOf(stored.column, sortColumns, defaultSort.column),
        direction: oneOf(stored.direction, ['asc', 'desc'], defaultSort.direction),
      }
    : defaultSort;
}

/** The choice kept in the browser; a field that is missing, broken or no longer known is as it is when nothing is chosen. */
export function loadMarketChoice(): MarketChoice {
  const stored = readStoredObject(key) ?? {};
  const sort = record(stored.sort);

  return {
    category: oneOf<CategoryFilter>(stored.category, ['all', 'linear', 'spot'], 'all'),
    setup: oneOf<SetupFilter>(
      stored.setup,
      ['all', ...setupChoices.map((item) => item.value)],
      'all',
    ),
    setupInterval: oneOf(
      stored.setupInterval,
      setupIntervals.map((item) => item.value),
      defaultSetupInterval,
    ),
    savedFilterId: typeof stored.savedFilterId === 'string' ? stored.savedFilterId : null,
    sort: sortOf(sort),
    pageSize: pageSizeOf(stored.pageSize),
  };
}

export function saveMarketChoice(choice: MarketChoice): void {
  const { category, setup, setupInterval, savedFilterId, sort, pageSize } = choice;

  writeStored(key, { category, setup, setupInterval, savedFilterId, sort, pageSize });
}
