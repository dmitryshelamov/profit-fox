import {
  type Change,
  type ComparisonItem,
  formatItems,
  parseItems,
  removeItem,
  toggleItem,
} from './items';

const KEY = 'profit-fox-comparison';

const listeners = new Set<() => void>();
// The set when the tab storage cannot be used (blocked site data); otherwise the storage is the source.
let memory = '';
let rejected = false;
let snapshot: { raw: string; items: readonly ComparisonItem[] } = { raw: '', items: [] };

function read(): string {
  try {
    return sessionStorage.getItem(KEY) ?? '';
  } catch {
    return memory;
  }
}

function write(items: readonly ComparisonItem[]) {
  rejected = false;
  memory = formatItems(items);

  try {
    sessionStorage.setItem(KEY, memory);
  } catch {
    // The set stays in memory until the tab closes.
  }

  listeners.forEach((listener) => listener());
}

/** The set of results marked for comparison; the same array until the set changes. */
export function comparisonSnapshot(): readonly ComparisonItem[] {
  const raw = read();

  if (raw !== snapshot.raw) {
    snapshot = { raw, items: parseItems(raw) };
  }

  return snapshot.items;
}

/** The last result the owner marked did not fit: the set is full. */
export function comparisonRejected(): boolean {
  return rejected;
}

export function subscribeComparison(listener: () => void): () => void {
  listeners.add(listener);

  return () => {
    listeners.delete(listener);
  };
}

/** Marks a result, or unmarks it when it is marked; `rejected` when the set is full. */
export function toggleComparison(item: ComparisonItem): Change {
  const change = toggleItem(comparisonSnapshot(), item);

  if (change.rejected) {
    rejected = true;
    listeners.forEach((listener) => listener());
  } else {
    write(change.items);
  }

  return change;
}

export function removeFromComparison(item: ComparisonItem) {
  write(removeItem(comparisonSnapshot(), item));
}

export function clearComparison() {
  write([]);
}

/** The address of the compare page replaces the set of the tab. */
export function replaceComparison(items: readonly ComparisonItem[]) {
  write(items);
}
