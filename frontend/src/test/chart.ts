import { act } from '@testing-library/react';

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
  /** The data of every series by its type, the last given (change add-ideas-and-market). */
  series: {} as Record<string, unknown[]>,
  /** The bars given one at a time to a series of candles: the live candle. */
  updates: [] as unknown[],
  /** The price lines on the series now. */
  lines: [] as { price: number; title?: string }[],
  /**
   * The lines of open ideas on the chart now (task 18.26), with their data and the labels written at their ends (task
   * 19.14).
   */
  segments: [] as {
    title: string;
    color?: string;
    data: unknown[];
    labels: { text: string; price: number; time: number; kind: string }[];
  }[],
  /** The marks of the series of those lines: the term of an idea. */
  segmentMarkers: [] as unknown[],
  /** The listener of the visible logical range of the time scale. */
  rangeChange: null as ((range: { from: number; to: number } | null) => void) | null,
  /** The logical range the chart was last told to show. */
  logicalRange: null as { from: number; to: number } | null,
  /** The options the last chart was created with (task 19.14). */
  options: {} as Record<string, unknown>,
  /** The options of the series of the panels by their type, as given and applied after. */
  seriesOptions: {} as Record<string, Record<string, unknown> | undefined>,
  /** The stretch of each pane: the candles, the open interest, the funding. */
  paneFactors: [] as number[],
  /** The room to the right of the last point of the time scale, in bars. */
  rightOffset: null as number | null,
  /** The margins of the price scales of the series by their type. */
  scaleMargins: {} as Record<string, unknown>,
  /** The empty points of the time scale after the last candle, up to the term of the open ideas (task 20.2). */
  slots: [] as { time: number }[],
  /** The width of the time scale of the last chart in pixels (task 22.1). */
  width: 900,
  /**
   * Changes the width of the time scale of the last chart on the next frame, before what waits for it, as a price scale
   * grown or shrunk by a new price does.
   */
  resize: (() => undefined) as (width: number) => void,
};

/** Forgets what the fake chart was given: the state of one test does not reach the next. */
export function resetDrawn(): void {
  Object.assign(drawn, {
    candles: [],
    markers: [],
    range: null,
    view: null,
    setData: 0,
    click: null,
    series: {},
    updates: [],
    lines: [],
    segments: [],
    segmentMarkers: [],
    rangeChange: null,
    logicalRange: null,
    options: {},
    seriesOptions: {},
    paneFactors: [],
    rightOffset: null,
    scaleMargins: {},
    slots: [],
    width: 900,
    resize: () => undefined,
  });
}

/**
 * Shows `range` as a user who scrolled or zoomed the chart does: once the chart drew what it was given, on a frame after
 * it (task 22.1).
 */
export async function scrollChart(range: { from: number; to: number }): Promise<void> {
  await nextFrame();
  drawn.logicalRange = range;
}

/** Waits for the next animation frame: the fake chart applies the range and the scroll position it was given then. */
export async function nextFrame(): Promise<void> {
  await act(() => new Promise<void>((resolve) => requestAnimationFrame(() => resolve())));
}

/** A click on the fake chart at the candle that opens at `time`, in seconds as the chart counts time. */
export function clickChart(time: number): void {
  if (drawn.click === null) {
    throw new Error('The chart listens to no clicks.');
  }

  drawn.click({ time });
}
