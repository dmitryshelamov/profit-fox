import type { Idea } from '@/api/ideas';
import type { ChartCandle, ChartPoint, LiveCandle } from '@/api/market';
import { lengthOf } from '@/preview/preview';

import { changeText } from './format';

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

/** The candles of two pages joined: the earlier page before the later, one candle each open time. */
export function joinPages(
  earlier: readonly ChartCandle[],
  later: readonly ChartCandle[],
): ChartCandle[] {
  const first = later.length > 0 ? Number(later[0]!.openTime) : Number.POSITIVE_INFINITY;
  return [...earlier.filter((candle) => Number(candle.openTime) < first), ...later];
}

/** The points of two pages joined by their time, each time once. */
export function joinPoints(
  earlier: readonly ChartPoint[],
  later: readonly ChartPoint[],
): ChartPoint[] {
  const first = later.length > 0 ? Number(later[0]!.time) : Number.POSITIVE_INFINITY;
  return [...earlier.filter((point) => Number(point.time) < first), ...later];
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
