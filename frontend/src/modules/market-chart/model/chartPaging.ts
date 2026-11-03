/** The length of each Bybit candle interval in seconds; a month is taken as its longest, 31 days, for paging only. */
export const intervalSeconds: Readonly<Record<string, number>> = {
  '1': 60,
  '3': 180,
  '5': 300,
  '15': 900,
  '30': 1800,
  '60': 3600,
  '120': 7200,
  '240': 14_400,
  '360': 21_600,
  '720': 43_200,
  D: 86_400,
  W: 604_800,
  M: 2_678_400,
};

/** Candles a chart asks for at once when it goes back in history. */
export const chartPageCandles = 500;

/** The most candles one request may cover; the server refuses a longer period. */
const maxPageCandles = 1000;

const duration = (interval: string) => (intervalSeconds[interval] ?? 3600) * 1000;

/** The UTC second a candle opened at `openSeconds` ends: a month ends where the next calendar month starts. */
export function candleEnd(openSeconds: number, interval: string): number {
  if (interval === 'M') {
    const open = new Date(openSeconds * 1000);

    return Date.UTC(open.getUTCFullYear(), open.getUTCMonth() + 1, 1) / 1000;
  }

  return openSeconds + (intervalSeconds[interval] ?? 3600);
}

/** The half-open UTC period of {@link chartPageCandles} candles that ends at `to`. */
export function pageBefore(to: string, interval: string): { from: string; to: string } {
  const end = Date.parse(to);

  return {
    from: new Date(end - chartPageCandles * duration(interval)).toISOString(),
    to: new Date(end).toISOString(),
  };
}

/** Whether the candles closed since `from` no longer fit in one request, so the chart must start over from now. */
export function tooFarBehind(from: string, now: Date, interval: string): boolean {
  return now.getTime() - Date.parse(from) > (maxPageCandles - 1) * duration(interval);
}

/** The loaded pages' candles as one history: each open time once, the later page wins, oldest first. */
export function mergeCandles<T extends { openTime: string }>(
  pages: readonly { candles: readonly T[] }[],
): T[] {
  const candles = new Map<number, T>();

  for (const page of pages) {
    for (const candle of page.candles) {
      candles.set(Date.parse(candle.openTime), candle);
    }
  }

  return [...candles.entries()].sort(([left], [right]) => left - right).map(([, candle]) => candle);
}
