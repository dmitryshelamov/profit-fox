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

export interface Sort {
  readonly column: SortColumn;
  readonly direction: SortDirection;
}
