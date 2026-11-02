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

export function intervalLabel(value: string): string {
  return intervals.find((interval) => interval.value === value)?.label ?? value;
}
