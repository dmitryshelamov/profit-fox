import { groupDigits } from '@/shared/format/format';

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;

function candleLength(interval: string): number {
  return interval === 'D' ? DAY : interval === 'W' ? 7 * DAY : Number(interval) * MINUTE;
}

function months(from: Date, to: Date): number {
  return (to.getUTCFullYear() - from.getUTCFullYear()) * 12 + to.getUTCMonth() - from.getUTCMonth();
}

/**
 * How many candles a point of the chart holds, from the distance between the opens of neighbouring points. A gap in
 * the candles only lengthens a distance, so the shortest one is the group size.
 */
export function candlesPerPoint(points: readonly { openTime: string }[], interval: string): number {
  let shortest = Number.POSITIVE_INFINITY;

  for (let index = 1; index < points.length; index++) {
    const from = new Date(points[index - 1]!.openTime);
    const to = new Date(points[index]!.openTime);
    const distance =
      interval === 'M'
        ? months(from, to)
        : Math.round((to.getTime() - from.getTime()) / candleLength(interval));

    shortest = Math.min(shortest, distance);
  }

  return Number.isFinite(shortest) ? shortest : 1;
}

function formOf(count: number, forms: readonly [string, string, string]): string {
  const rest = count % 100;
  const last = count % 10;

  return rest >= 11 && rest <= 14
    ? forms[2]
    : last === 1
      ? forms[0]
      : last >= 2 && last <= 4
        ? forms[1]
        : forms[2];
}

/** `96 свечей`, with the form of the word that goes with the count. */
export function candlesText(count: number): string {
  return `${count} ${formOf(count, ['свеча', 'свечи', 'свечей'])}`;
}

/** `по 106 свечей в точке`. */
export function perPointText(count: number): string {
  return `по ${count} ${formOf(count, ['свече', 'свечи', 'свечей'])} в точке`;
}

/** `5 000 точек`. */
export function pointsText(count: number): string {
  return `${groupDigits(String(count))} ${formOf(count, ['точка', 'точки', 'точек'])}`;
}
