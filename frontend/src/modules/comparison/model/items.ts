/** A result of a backtest: its number among the combinations of the grid, counted from zero. */
export interface ComparisonItem {
  readonly backtestId: string;
  readonly combination: number;
}

export interface Change {
  readonly items: readonly ComparisonItem[];
  /** The set was full and the result was not added. */
  readonly rejected: boolean;
}

/** At most this many results fit side by side. */
export const COMPARISON_LIMIT = 6;

const same = (a: ComparisonItem, b: ComparisonItem) =>
  a.backtestId === b.backtestId && a.combination === b.combination;

export function addItem(items: readonly ComparisonItem[], item: ComparisonItem): Change {
  if (items.some((entry) => same(entry, item))) {
    return { items, rejected: false };
  }

  return items.length >= COMPARISON_LIMIT
    ? { items, rejected: true }
    : { items: [...items, item], rejected: false };
}

export function removeItem(
  items: readonly ComparisonItem[],
  item: ComparisonItem,
): readonly ComparisonItem[] {
  return items.filter((entry) => !same(entry, item));
}

export function toggleItem(items: readonly ComparisonItem[], item: ComparisonItem): Change {
  return items.some((entry) => same(entry, item))
    ? { items: removeItem(items, item), rejected: false }
    : addItem(items, item);
}

/** The value of the `items` address parameter: `<id>:<number>` pairs separated by commas. */
export function formatItems(items: readonly ComparisonItem[]): string {
  return items.map((item) => `${item.backtestId}:${item.combination}`).join(',');
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Reads the address back; malformed and repeated entries are skipped and the list is cut at the limit. */
export function parseItems(value: string | undefined): readonly ComparisonItem[] {
  let items: readonly ComparisonItem[] = [];

  for (const entry of (value ?? '').split(',')) {
    const [backtestId = '', number = ''] = entry.split(':');

    if (uuid.test(backtestId) && /^\d{1,9}$/.test(number)) {
      items = addItem(items, { backtestId, combination: Number(number) }).items;
    }
  }

  return items;
}
