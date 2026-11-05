import { intervalSeconds } from './chartPaging';

export type LevelKind = 'entry' | 'stop' | 'target';

export interface LevelInput {
  readonly kind: LevelKind;
  readonly price: string;
  readonly title: string;
}

export interface LevelLine {
  readonly kind: LevelKind;
  readonly price: number;
  readonly title: string;
}

export interface ZoneInput {
  readonly low: string;
  readonly high: string;
  readonly kind: string;
}

export interface ZoneBand {
  readonly low: number;
  readonly high: number;
  readonly kind: string;
}

/** The levels of an idea as price lines; decimal strings become numbers here, for drawing only. */
export function levelLines(levels: readonly LevelInput[]): LevelLine[] {
  return levels.map((level) => ({ ...level, price: Number(level.price) }));
}

/** The zones of support and resistance as bands. */
export function zoneBands(zones: readonly ZoneInput[]): ZoneBand[] {
  return zones.map((zone) => ({ low: Number(zone.low), high: Number(zone.high), kind: zone.kind }));
}

/** The UTC second the candle that holds `firedAt` opened at, where the mark of the idea stands. */
export function firedMark(firedAt: string, interval: string): number {
  const length = intervalSeconds[interval] ?? 3600;

  return Math.floor(Date.parse(firedAt) / 1000 / length) * length;
}

/** The vertical extent of a band in pixels, or `null` while the chart cannot place one of its prices. */
export function bandRect(
  band: { low: number; high: number },
  toY: (price: number) => number | null,
): { top: number; height: number } | null {
  const high = toY(band.high);
  const low = toY(band.low);

  return high === null || low === null
    ? null
    : { top: Math.min(high, low), height: Math.abs(low - high) };
}

export interface PriceRange {
  readonly minValue: number;
  readonly maxValue: number;
}

/**
 * The autoscale info of the candles widened to hold the prices of the levels, so that the lines of an idea stay in view
 * even when the entry or the target is far from the candles. Without levels the info is returned as it is.
 */
export function withLevels<T extends { priceRange: PriceRange | null }>(
  info: T | null,
  prices: readonly number[],
): T | { priceRange: PriceRange } | null {
  if (prices.length === 0) {
    return info;
  }

  return {
    ...info,
    priceRange: {
      minValue: Math.min(...prices, info?.priceRange?.minValue ?? Infinity),
      maxValue: Math.max(...prices, info?.priceRange?.maxValue ?? -Infinity),
    },
  };
}
