import type { Idea } from '@/api/ideas';
import type { ChartCandle, ChartPoint, LiveCandle } from '@/api/market';
import { lengthOf } from '@/preview/preview';

import { changeText, momentText, priceWords, rateText } from './format';

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

/** The height of the chart of a pair in pixels. */
export const chartHeight = 600;

/**
 * How the height of the chart goes to its panes (task 19.14): the candles, then the open interest and the funding about 90
 * pixels each, as in the mockup. Stretch factors, not heights: a height set before the chart is laid out is lost.
 */
export const paneStretch = { candles: 4.5, panel: 1 } as const;

/** The margins of the scales of the panels: their marks stay inside the panel, off the edges it shares (task 19.14). */
export const panelMargins = { top: 0.25, bottom: 0.15 } as const;

/** The height in pixels a panel gets of a chart of `height` with a time scale of `timeScale` pixels. */
export function panelHeight(height = chartHeight, timeScale = 28): number {
  return ((height - timeScale) * paneStretch.panel) / (paneStretch.candles + 2 * paneStretch.panel);
}

/** A price on the scale of the chart, written as the bench writes prices: with a point (task 19.14). */
export function scalePrice(price: number): string {
  return priceWords(price);
}

/** The smallest step of the prices of the scale: five significant digits of the price, as the bench shows them. */
export function minMoveOf(price: number | undefined): number {
  if (price === undefined || !Number.isFinite(price) || price <= 0) {
    return 0.01;
  }

  return Math.min(1, 10 ** (Math.floor(Math.log10(price)) - 4));
}

/** The open interest in USDT on its scale: three significant digits, in millions or billions — `1.15 млрд`. */
export function scaleAmount(value: number): string {
  const size = Math.abs(value);
  const [amount, unit] =
    size >= 1e9 ? [value / 1e9, 'млрд'] : size >= 1e6 ? [value / 1e6, 'млн'] : [value / 1e3, 'тыс'];
  return `${Number(amount.toPrecision(3))} ${unit}`;
}

/** A payment of funding on its scale: in percent with four digits — `0.0300 %`, not the share. */
export function scaleRate(rate: number): string {
  return rateText(rate);
}

/** A label of an open idea, written to the right of the end of its line (task 19.14), as the mockup does. */
export interface LineLabel {
  readonly id: string;
  readonly kind: IdeaLine['kind'] | 'term';
  /** The time of the end of the line, in milliseconds. */
  readonly time: number;
  readonly price: number;
  readonly text: string;
}

/**
 * The labels of the lines of the open ideas: «вход», «стоп», «цель» at the end of their lines, and «срок» at the end
 * halfway between the entry and the line above it — or with the entry, when no line is above it.
 */
export function lineLabels(lines: readonly IdeaLine[]): LineLabel[] {
  return lines.flatMap((line) => {
    const label: LineLabel = {
      id: line.id,
      kind: line.kind,
      time: line.to,
      price: line.price,
      text: line.title,
    };
    if (line.term === undefined) {
      return [label];
    }

    const top = Math.max(
      ...lines.filter((other) => other.id === line.id).map((other) => other.price),
    );
    return top > line.price
      ? [label, { ...label, kind: 'term' as const, price: (line.price + top) / 2, text: line.term }]
      : [{ ...label, text: `${line.title} · ${line.term}` }];
  });
}

/** How wide a label of a line is, in pixels, at 11 pixels of a monospace font. */
const labelWidth = (text: string) => text.length * 7 + 12;

/**
 * The room to the right of the last candle (task 19.14): the lines of the open ideas end at their term, but no further
 * than a third of the bars in view from the last candle; to the right of the end there is room for their labels, and a
 * little room for the marks of the last candles without lines. `offset` is in bars past the last point of the time scale.
 */
export function rightRoom(
  lines: readonly IdeaLine[],
  lastOpen: number | undefined,
  timeframe: string,
  barsInView: number,
  barSpacing: number,
): { lines: IdeaLine[]; offset: number } {
  const length = lengthOf(timeframe);
  const most = Math.max(1, Math.floor(barsInView / 3));
  const until = lastOpen === undefined ? Number.POSITIVE_INFINITY : lastOpen + most * length;
  const drawn = lines.map((line) => (line.to > until ? { ...line, to: until } : line));
  const ahead =
    lastOpen === undefined
      ? 0
      : Math.max(0, ...drawn.map((line) => Math.round((line.to - lastOpen) / length)));
  const labels = lineLabels(drawn);
  const room =
    labels.length === 0
      ? Math.ceil(60 / barSpacing)
      : Math.ceil(Math.max(...labels.map((label) => labelWidth(label.text))) / barSpacing);
  return { lines: drawn, offset: Math.max(0, Math.min(room, most - ahead)) };
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
