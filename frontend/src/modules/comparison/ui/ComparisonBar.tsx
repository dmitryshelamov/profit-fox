import { Link } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';

import { COMPARISON_LIMIT, formatItems } from '../model/items';
import {
  clearComparison,
  comparisonRejected,
  comparisonSnapshot,
  subscribeComparison,
} from '../model/store';

/** The set of results marked for comparison, as the bar every page shows while the set is not empty. */
export function ComparisonBar() {
  const items = useSyncExternalStore(subscribeComparison, comparisonSnapshot);
  const rejected = useSyncExternalStore(subscribeComparison, comparisonRejected);

  if (items.length === 0) {
    return null;
  }

  return (
    <section
      aria-label="Сравнение"
      className="fixed bottom-6 left-1/2 z-40 flex -translate-x-1/2 items-center gap-4 rounded-xl border-2 border-accent bg-panel px-5 py-3 shadow-2xl"
    >
      <span className="font-bold">Сравнение</span>
      <span className="rounded-full bg-[#3a2c12] px-2.5 py-0.5 text-xs font-bold text-accent">
        {items.length} из {COMPARISON_LIMIT}
      </span>
      {rejected && (
        <span role="status" className="text-xs text-warn">
          В наборе не больше шести результатов.
        </span>
      )}
      <button
        type="button"
        className="ml-6 text-sm text-muted hover:text-text"
        onClick={clearComparison}
      >
        Очистить
      </button>
      <Link
        to="/compare"
        search={{ items: formatItems(items) }}
        className="inline-flex h-8 items-center rounded-md bg-accent px-3 text-sm font-bold text-on-accent hover:brightness-110"
      >
        Сравнить →
      </Link>
    </section>
  );
}
