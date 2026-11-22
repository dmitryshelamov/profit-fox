/**
 * What the fake chart of the tests was given last: the candles, the marks, the shown range, how it was last told what
 * to show — all of its content or a range — and how many times candles were set.
 */
export const drawn = {
  candles: [] as unknown[],
  markers: [] as unknown[],
  range: null as { from: number; to: number } | null,
  view: null as 'content' | 'range' | null,
  setData: 0,
  click: null as ((param: { time?: number }) => void) | null,
};

/** A click on the fake chart at the candle that opens at `time`, in seconds as the chart counts time. */
export function clickChart(time: number): void {
  if (drawn.click === null) {
    throw new Error('The chart listens to no clicks.');
  }

  drawn.click({ time });
}
