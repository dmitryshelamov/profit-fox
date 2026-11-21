/** What the fake chart of the tests was given last: the candles, the marks, the shown range, and how many times candles were set. */
export const drawn = {
  candles: [] as unknown[],
  markers: [] as unknown[],
  range: null as { from: number; to: number } | null,
  setData: 0,
};
