import type { MarketRow } from './rows';

export const sortColumns = [
  'symbol',
  'price',
  'change5m',
  'change15m',
  'change1h',
  'change4h',
  'change24h',
  'turnover1h',
  'turnover24h',
  'openInterest',
  'openInterestChange1h',
  'openInterestChange24h',
  'fundingRate',
  'spread',
] as const;

export type SortColumn = (typeof sortColumns)[number];

export type SortDirection = 'asc' | 'desc';

/** Sorts by a column in either direction; pairs with an empty value in it come last whichever way it goes. */
export function sortRows(
  rows: readonly MarketRow[],
  column: SortColumn,
  direction: SortDirection,
): MarketRow[] {
  const sign = direction === 'asc' ? 1 : -1;
  const valueOf = (row: MarketRow) => (column === 'symbol' ? row.symbol : Number(row[column]));

  return rows
    .filter((row) => row[column] !== null)
    .sort((left, right) => {
      const a = valueOf(left);
      const b = valueOf(right);

      return a < b ? -sign : a > b ? sign : 0;
    })
    .concat(rows.filter((row) => row[column] === null));
}
