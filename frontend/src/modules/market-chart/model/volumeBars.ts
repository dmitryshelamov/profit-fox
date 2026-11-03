import type { Bar } from './liveCandles';

/** A volume column: a candle that closed at or above its open is rising. */
export interface VolumeBar {
  readonly time: number;
  readonly value: number;
  readonly rising: boolean;
  readonly forming: boolean;
}

export function volumeBars(bars: readonly Bar[]): VolumeBar[] {
  return bars.map((bar) => ({
    time: bar.time,
    value: bar.volume,
    rising: bar.close >= bar.open,
    forming: bar.forming,
  }));
}
