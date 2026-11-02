/** One chart point: a candle, or a group of consecutive candles, with the account's equity after it. */
export interface PricePoint {
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly equity: string;
}

export interface DrawnCandle {
  readonly time: number;
  readonly open: number;
  readonly high: number;
  readonly low: number;
  readonly close: number;
}

export interface DrawnValue {
  readonly time: number;
  readonly value: number;
}

export interface Fill {
  /** UTC time of the fill. */
  readonly at: string;
  readonly side: 'buy' | 'sell';
}

export interface FillMarker {
  /** UTC seconds of the point the fills belong to. */
  readonly time: number;
  readonly side: 'buy' | 'sell';
  readonly kind: 'entry' | 'exit';
  /** How many fills of this side the point holds. */
  readonly count: number;
}

const seconds = (iso: string) => Math.floor(Date.parse(iso) / 1000);

/** The points the chart can draw: the library needs strictly increasing times, so a repeated or earlier one is dropped. */
function ordered<T extends { openTime: string }>(
  points: readonly T[],
): { time: number; point: T }[] {
  const kept: { time: number; point: T }[] = [];
  let previous = Number.NEGATIVE_INFINITY;

  for (const point of points) {
    const time = seconds(point.openTime);

    if (time > previous) {
      previous = time;
      kept.push({ time, point });
    }
  }

  return kept;
}

/**
 * Turns exact decimal strings into the numbers the chart library draws. This is the only place they become numbers:
 * the chart shows shapes, and every exact value on the page comes from the strings.
 */
export function drawable(points: readonly PricePoint[]): {
  candles: DrawnCandle[];
  equity: DrawnValue[];
} {
  const kept = ordered(points);

  return {
    candles: kept.map(({ time, point }) => ({
      time,
      open: Number(point.open),
      high: Number(point.high),
      low: Number(point.low),
      close: Number(point.close),
    })),
    equity: kept.map(({ time, point }) => ({ time, value: Number(point.equity) })),
  };
}

/** The equity of a result as the chart draws it, for a comparison of several results. */
export function drawableEquity(
  points: readonly { openTime: string; equity: string }[],
): DrawnValue[] {
  return ordered(points).map(({ time, point }) => ({ time, value: Number(point.equity) }));
}

/**
 * Marks the fills on the points: a fill belongs to the point whose `openTime` is the latest not after it, so a fill
 * exactly at the start of a group is in that group. A point holds one marker per side with the number of fills.
 * Whether a side enters or leaves the position depends on the strategy's direction.
 */
export function markersOf(
  points: readonly PricePoint[],
  fills: readonly Fill[],
  direction: 'long' | 'short',
): FillMarker[] {
  const kept = ordered(points);
  const counts = kept.map(() => ({ buy: 0, sell: 0 }));

  for (const fill of fills) {
    const at = seconds(fill.at);
    const index = kept.findLastIndex(({ time }) => time <= at);

    if (index >= 0) {
      counts[index]![fill.side] += 1;
    }
  }

  const entrySide = direction === 'long' ? 'buy' : 'sell';

  return kept.flatMap(({ time }, index) =>
    (['buy', 'sell'] as const)
      .filter((side) => counts[index]![side] > 0)
      .map((side) => ({
        time,
        side,
        kind: side === entrySide ? ('entry' as const) : ('exit' as const),
        count: counts[index]![side],
      })),
  );
}
