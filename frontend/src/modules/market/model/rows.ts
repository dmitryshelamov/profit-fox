import type { Schemas } from '@/shared/api/client';

export type MarketRow = Schemas['MarketRowResponse'];

export type CategoryFilter = 'all' | 'linear' | 'spot';

export interface CategoryOption {
  readonly value: CategoryFilter;
  readonly label: string;
  readonly count: number;
}

const countOf = (rows: readonly MarketRow[], category: string) =>
  rows.filter((row) => row.category === category).length;

/** The category filter: all pairs, then each category, each with its number of pairs. */
export function categoryOptions(rows: readonly MarketRow[]): CategoryOption[] {
  return [
    { value: 'all', label: 'Все', count: rows.length },
    { value: 'linear', label: 'linear', count: countOf(rows, 'linear') },
    { value: 'spot', label: 'spot', count: countOf(rows, 'spot') },
  ];
}

/** The pairs of the category whose symbol contains the search text, without regard to case. */
export function filterRows(
  rows: readonly MarketRow[],
  category: CategoryFilter,
  search: string,
): MarketRow[] {
  const needle = search.toUpperCase();

  return rows.filter(
    (row) =>
      (category === 'all' || row.category === category) &&
      row.symbol.toUpperCase().includes(needle),
  );
}
