import { setupChoices } from '@/modules/market';
import { pageSizeOf } from '@/shared/paging/pageSizes';
import { oneOf, readStoredObject, writeStored } from '@/shared/storage/storedChoice';

import { type JournalFilters, noJournalFilters } from '../api';

const key = 'profit-fox.idea-journal.choice';

/** What the owner chose on the journal: the filters and the size of a page. */
export interface JournalChoice {
  readonly filters: JournalFilters;
  readonly pageSize: number;
}

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};

/** The choice kept in the browser; a field that is missing, broken or no longer known is as it is when nothing is chosen. */
export function loadJournalChoice(): JournalChoice {
  const stored = readStoredObject(key) ?? {};
  const filters = record(stored.filters);

  return {
    filters: {
      setup: oneOf(
        filters.setup,
        ['', ...setupChoices.map((item) => item.value)],
        noJournalFilters.setup,
      ),
      symbol: typeof filters.symbol === 'string' ? filters.symbol : noJournalFilters.symbol,
      side: oneOf(filters.side, ['', 'long', 'short'], noJournalFilters.side),
      badge: oneOf(filters.badge, ['', 'up', 'flat', 'down', 'none'], noJournalFilters.badge),
      days: oneOf(filters.days, ['', '7', '30', '90'], noJournalFilters.days),
      origin: oneOf(filters.origin, ['', 'manual', 'auto', 'paper'], noJournalFilters.origin),
      autoId: typeof filters.autoId === 'string' ? filters.autoId : noJournalFilters.autoId,
    },
    pageSize: pageSizeOf(stored.pageSize),
  };
}

export function saveJournalChoice(choice: JournalChoice): void {
  writeStored(key, choice);
}
