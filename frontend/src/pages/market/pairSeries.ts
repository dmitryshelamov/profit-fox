import type { Idea } from '@/api/ideas';
import type { ChartCandle, ChartPoint, LiveCandle } from '@/api/market';
import { lengthOf } from '@/preview/preview';

import { changeText, momentText, priceWords } from './format';

/** The words of the reason an idea closed, as its mark on the chart says it. */
const exitMarkWords: Record<string, string> = {
  stop: 'по стопу',
  target: 'по цели',
  time: 'по сроку',
};

/** The open time of the candle of the timeframe that a moment falls in, in milliseconds. */
export function candleOf(ms: number, timeframe: string): number {
  const length = lengthOf(timeframe);
  return Math.floor(ms / length) * length;
}

/** A mark of an idea on the chart: its entry or its exit, on the candle of the timeframe the moment falls in. */
export interface IdeaMark {
  readonly id: string;
  readonly time: number;
  readonly kind: 'entry' | 'exit';
  readonly side: string;
  readonly text: string;
}

/**
 * The marks of the ideas of a pair (spec market-overview «Идеи на графике»): the entry with its side and strategy on the
 * candle of the moment of the idea, and the exit with its reason and outcome on the candle of the exit — on the
 * candle of the shown timeframe each moment falls in, whatever the timeframe of the idea.
 */
export function ideaMarks(ideas: readonly Idea[], timeframe: string): IdeaMark[] {
  return ideas.flatMap((idea) => {
    const entry: IdeaMark = {
      id: idea.id,
      time: candleOf(Number(idea.moment), timeframe),
      kind: 'entry',
      side: idea.side,
      text: `${idea.side === 'short' ? '▼ шорт' : '▲ лонг'} · ${idea.strategy}`,
    };
    if (idea.state !== 'closed' || idea.exitTime == null) {
      return [entry];
    }

    return [
      entry,
      {
        id: idea.id,
        time: candleOf(Number(idea.exitTime), timeframe),
        kind: 'exit',
        side: idea.side,
        text: `выход ${exitMarkWords[idea.exitReason ?? ''] ?? ''} ${changeText(idea.result)}`.replace(
          /\s+/g,
          ' ',
        ),
      },
    ];
  });
}

/** A line of an open idea: its entry, stop or target at its price from the candle of its entry to that of its term. */
export interface IdeaLine {
  readonly id: string;
  readonly kind: 'entry' | 'stop' | 'target';
  readonly price: number;
  readonly title: string;
  readonly from: number;
  readonly to: number;
  /** The mark of the term at the end of the line of the entry: «срок 17:00». */
  readonly term?: string;
}

/**
 * The lines of the open ideas (spec market-overview «Открытая идея», task 18.26): the entry, the stop and the target
 * with their prices, each from the candle the moment of the idea falls in to the candle its term falls in, and the term
 * marked at the end of the line of the entry.
 */
export function ideaLines(ideas: readonly Idea[], timeframe: string): IdeaLine[] {
  return ideas.flatMap((idea) => {
    const from = candleOf(Number(idea.moment), timeframe);
    const to = Math.max(from, candleOf(Number(idea.until), timeframe));
    const term = `срок ${momentText(idea.until).slice(6)}`;
    const line = (
      kind: IdeaLine['kind'],
      price: number | string | null | undefined,
      title: string,
    ) =>
      price === null || price === undefined
        ? []
        : [
            {
              id: idea.id,
              kind,
              price: Number(price),
              title,
              from,
              to,
              ...(kind === 'entry' ? { term } : {}),
            },
          ];
    return [
      ...line('entry', idea.entry, `вход ${priceWords(idea.entry)}`),
      ...line('stop', idea.stop, `стоп ${priceWords(idea.stop)}`),
      ...line('target', idea.target, `цель ${priceWords(idea.target)}`),
    ];
  });
}

/** The candles of two pages joined by their open time, each once, the later page's candle kept. */
export function joinPages(
  earlier: readonly ChartCandle[],
  later: readonly ChartCandle[],
): ChartCandle[] {
  return joinByTime(earlier, later, (candle) => Number(candle.openTime));
}

/** The points of two pages joined by their time, each time once, the later page's point kept. */
export function joinPoints(
  earlier: readonly ChartPoint[],
  later: readonly ChartPoint[],
): ChartPoint[] {
  return joinByTime(earlier, later, (point) => Number(point.time));
}

// Joined by time (task 19.11): every item of the later page, and those of the earlier one at times the later one does not
// have — a first page read again moved right, and the candles of the former first page it no longer has stay.
function joinByTime<T>(earlier: readonly T[], later: readonly T[], time: (item: T) => number): T[] {
  const times = new Set(later.map(time));
  return [...earlier.filter((item) => !times.has(time(item))), ...later].sort(
    (a, b) => time(a) - time(b),
  );
}

/**
 * The open interest in USDT on the candles: the coins of each point times the close of the candle it stands on (spec
 * market-overview «OI и фандинг под графиком»).
 */
export function interestInUsdt(
  points: readonly ChartPoint[],
  candles: readonly ChartCandle[],
): ChartPoint[] {
  const closes = new Map(candles.map((candle) => [Number(candle.openTime), Number(candle.close)]));
  return points.flatMap((point) => {
    const close = closes.get(Number(point.time));
    return close === undefined ? [] : [{ time: point.time, value: Number(point.value) * close }];
  });
}

/**
 * The payments of funding on the candles of the timeframe (task 18.25): each on the open time of the candle it falls in,
 * those of one candle summed. A point at a time no candle has would put an empty slot into the row of candles of the
 * common time scale — a day of 1d holds three payments, an hour of funding is not a candle of 4h.
 */
export function fundingOnCandles(points: readonly ChartPoint[], timeframe: string): ChartPoint[] {
  const sums = new Map<number, number>();
  for (const point of points) {
    const time = candleOf(Number(point.time), timeframe);
    sums.set(time, (sums.get(time) ?? 0) + Number(point.value));
  }

  return [...sums.entries()].sort(([a], [b]) => a - b).map(([time, value]) => ({ time, value }));
}

/** The candles with the live one in place: a candle of the same open time replaced, a new one added at the end. */
export function withLive(candles: readonly ChartCandle[], live: LiveCandle | null): ChartCandle[] {
  if (live === null) {
    return [...candles];
  }

  const bar: ChartCandle = {
    openTime: live.openTime,
    open: live.open,
    high: live.high,
    low: live.low,
    close: live.close,
    volume: live.volume,
  };
  const last = candles.at(-1);
  if (last !== undefined && Number(last.openTime) === Number(live.openTime)) {
    return [...candles.slice(0, -1), bar];
  }

  return last === undefined || Number(live.openTime) > Number(last.openTime)
    ? [...candles, bar]
    : [...candles];
}
