/** The market categories, as the API takes them: the two of Bybit and the shares of MOEX. */
export const marketCategories = ['spot', 'linear', 'moex'] as const;

export type MarketCategory = (typeof marketCategories)[number];

/** The candle intervals of Bybit, as the API takes them, with their titles. */
export const intervals = [
  { value: '1', label: '1 мин' },
  { value: '3', label: '3 мин' },
  { value: '5', label: '5 мин' },
  { value: '15', label: '15 мин' },
  { value: '30', label: '30 мин' },
  { value: '60', label: '1 ч' },
  { value: '120', label: '2 ч' },
  { value: '240', label: '4 ч' },
  { value: '360', label: '6 ч' },
  { value: '720', label: '12 ч' },
  { value: 'D', label: '1 д' },
  { value: 'W', label: '1 нед' },
  { value: 'M', label: '1 мес' },
] as const;

/** T-Invest gives candles up to four hours; a daily candle of MOEX opens off the UTC grid, so it is not offered. */
const moexIntervals = intervals.filter((interval) =>
  ['1', '3', '5', '15', '30', '60', '120', '240'].includes(interval.value),
);

/** The intervals of the candles of a category: every interval of Bybit, eight for moex. */
export function intervalsOf(category: string): readonly { value: string; label: string }[] {
  return category === 'moex' ? moexIntervals : intervals;
}

export function intervalLabel(value: string): string {
  return intervals.find((interval) => interval.value === value)?.label ?? value;
}
