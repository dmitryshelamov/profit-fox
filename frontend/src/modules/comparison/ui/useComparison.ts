import { useSyncExternalStore } from 'react';

import type { ComparisonItem } from '../model/items';
import { comparisonSnapshot, subscribeComparison } from '../model/store';

/** The results marked for comparison; the component renders again when the set changes. */
export function useComparison(): readonly ComparisonItem[] {
  return useSyncExternalStore(subscribeComparison, comparisonSnapshot);
}
