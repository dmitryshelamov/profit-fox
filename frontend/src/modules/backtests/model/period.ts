const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;

export type PeriodLength = 'month' | 'quarter' | 'year';

const days: Record<'month' | 'quarter', number> = { month: 30, quarter: 90 };

/** Start of the candle that is still open at `now`: everything before it is closed. `interval` is a Bybit interval. */
export function lastClosedBoundary(now: Date, interval: string): Date {
  const midnight = Math.floor(now.getTime() / DAY) * DAY;

  if (interval === 'D') {
    return new Date(midnight);
  }

  if (interval === 'W') {
    // Weeks start on Monday; 1970-01-01 was a Thursday, so the weekday of a day count d is (d + 3) mod 7 from Monday.
    const sinceMonday = (Math.floor(midnight / DAY) + 3) % 7;

    return new Date(midnight - sinceMonday * DAY);
  }

  if (interval === 'M') {
    return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
  }

  const length = Number(interval) * MINUTE;

  return new Date(midnight + Math.floor((now.getTime() - midnight) / length) * length);
}

/** A period of the given length that ends at the last closed candle. */
export function periodEndingAt(
  now: Date,
  interval: string,
  length: PeriodLength,
): { from: Date; to: Date } {
  const to = lastClosedBoundary(now, interval);

  if (length === 'year') {
    const from = new Date(to);
    from.setUTCFullYear(from.getUTCFullYear() - 1);

    return { from, to };
  }

  return { from: new Date(to.getTime() - days[length] * DAY), to };
}
