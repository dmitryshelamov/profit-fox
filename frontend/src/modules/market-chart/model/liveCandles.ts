/** A candle as exact decimal strings: a closed one from the history or one the live channel sends. */
export interface BarInput {
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly volume: string;
}

/** A candle of the live channel: the current one, or one just closed that the history does not have yet. */
export interface LiveCandle extends BarInput {
  readonly isClosed: boolean;
}

/** A candle as the chart draws it: UTC seconds and numbers; `forming` marks the one that is not closed. */
export interface Bar {
  readonly time: number;
  readonly open: number;
  readonly high: number;
  readonly low: number;
  readonly close: number;
  readonly volume: number;
  readonly forming: boolean;
}

/**
 * Turns exact decimal strings into the numbers the chart draws. The chart shows shapes; every exact value on the page
 * comes from the strings.
 */
export function drawnBars(candles: readonly BarInput[], forming: boolean): Bar[] {
  return candles.map((candle) => ({
    time: Math.floor(Date.parse(candle.openTime) / 1000),
    open: Number(candle.open),
    high: Number(candle.high),
    low: Number(candle.low),
    close: Number(candle.close),
    volume: Number(candle.volume),
    forming,
  }));
}

/**
 * The live candles to draw after the closed history, oldest first. The history stays authoritative: a live candle that
 * opened at or before its last candle is left out, so a candle that has since closed is never drawn twice.
 */
export function liveCandles(
  history: readonly BarInput[],
  live: readonly LiveCandle[],
): LiveCandle[] {
  const last = history.at(-1);
  const after = last ? Date.parse(last.openTime) : Number.NEGATIVE_INFINITY;

  return live.filter((candle) => Date.parse(candle.openTime) > after);
}
