import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  HistogramSeries,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  LineSeries,
  LineStyle,
  type LogicalRange,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import type { ChartCandle, ChartPoint, LiveCandle } from '@/api/market';
import { chartColors } from '@/preview/CandleChart';
import { priceText } from '@/preview/chartFormat';

import { amountText } from './format';
import type { IdeaLine, IdeaMark } from './pairSeries';

/** A rate of funding at or above this share, by its size, is lit in the panel (spec «OI и фандинг под графиком»). */
export const highPayment = 0.0003;

/** The colors of the lines of an open idea: the entry, the stop and the target. */
const lineColors: Record<IdeaLine['kind'], string> = {
  entry: chartColors.text,
  stop: chartColors.down,
  target: chartColors.up,
};

/** The color of the open interest: the token `run` of the theme. */
const interestColor = '#7AA7FF';

/** How close to the left edge, in bars, the chart asks for the page before. */
export const edgeBars = 10;

interface Drawn {
  readonly chart: IChartApi;
  readonly candles: ISeriesApi<'Candlestick'>;
  readonly interest: ISeriesApi<'Line'>;
  readonly funding: ISeriesApi<'Histogram'>;
  readonly markers: ISeriesMarkersPluginApi<Time>;
}

const seconds = (ms: number | string) => (Number(ms) / 1000) as UTCTimestamp;

/**
 * The chart of a pair (spec market-overview «График пары», «Живая свеча», «Идеи на графике», «OI и фандинг под
 * графиком»): the candles with the live one updated in place, the marks of the ideas, the lines of the chosen open idea,
 * and under the candles the panes of the open interest in USDT and of the payments of funding on the same time scale.
 * Scrolled to its left edge it asks for the page before by `onEdge` and keeps the part in view when the page comes.
 */
export function PairChart({
  candles,
  live,
  interest,
  funding,
  marks,
  selected,
  lines,
  onEdge,
  onPick,
}: {
  readonly candles: readonly ChartCandle[];
  readonly live: LiveCandle | null;
  readonly interest: readonly ChartPoint[];
  readonly funding: readonly ChartPoint[];
  readonly marks: readonly IdeaMark[];
  readonly selected: string | null;
  /** The lines of the open ideas: entry, stop and target from the candle of the entry to that of the term. */
  readonly lines: readonly IdeaLine[];
  readonly onEdge: () => void;
  readonly onPick: (id: string) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<Drawn | null>(null);
  const latest = useRef({ marks, onEdge, onPick });
  latest.current = { marks, onEdge, onPick };
  const shown = useRef<{ first: number | null; count: number }>({ first: null, count: 0 });

  useEffect(() => {
    if (container.current === null) {
      return;
    }

    const chart = createChart(container.current, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: chartColors.background },
        textColor: chartColors.text,
      },
      grid: { vertLines: { color: chartColors.grid }, horzLines: { color: chartColors.grid } },
      localization: { locale: 'ru-RU', priceFormatter: priceText },
      timeScale: { timeVisible: true, secondsVisible: false },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: chartColors.up,
      downColor: chartColors.down,
      borderVisible: false,
      wickUpColor: chartColors.up,
      wickDownColor: chartColors.down,
    });
    const interestSeries = chart.addSeries(
      LineSeries,
      {
        color: interestColor,
        lineWidth: 2,
        priceFormat: { type: 'custom', formatter: amountText },
      },
      1,
    );
    const fundingSeries = chart.addSeries(
      HistogramSeries,
      {
        priceFormat: {
          type: 'custom',
          formatter: (rate: number) => `${(rate * 100).toFixed(4)} %`,
        },
      },
      2,
    );
    const [, interestPane, fundingPane] = chart.panes();
    interestPane?.setHeight(90);
    fundingPane?.setHeight(80);

    const click = (param: { time?: Time }) => {
      const mark = latest.current.marks.find(
        (candidate) => candidate.time / 1000 === Number(param.time),
      );
      if (mark !== undefined) {
        latest.current.onPick(mark.id);
      }
    };
    const scroll = (range: LogicalRange | null) => {
      if (range !== null && range.from < edgeBars) {
        latest.current.onEdge();
      }
    };
    chart.subscribeClick(click);
    chart.timeScale().subscribeVisibleLogicalRangeChange(scroll);
    drawn.current = {
      chart,
      candles: series,
      interest: interestSeries,
      funding: fundingSeries,
      markers: createSeriesMarkers(series, []),
    };

    return () => {
      drawn.current = null;
      chart.unsubscribeClick(click);
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(scroll);
      chart.remove();
    };
  }, []);

  // A page before the shown candles keeps the part in view: the logical range moves by the candles added on the left.
  useEffect(() => {
    const current = drawn.current;
    if (current === null) {
      return;
    }

    const first = candles.length > 0 ? Number(candles[0]!.openTime) : null;
    const before = shown.current;
    const added =
      before.first !== null && first !== null && first < before.first
        ? candles.length - before.count
        : 0;
    const range = current.chart.timeScale().getVisibleLogicalRange();
    current.candles.setData(
      candles.map((candle) => ({
        time: seconds(candle.openTime),
        open: Number(candle.open),
        high: Number(candle.high),
        low: Number(candle.low),
        close: Number(candle.close),
      })),
    );
    if (added > 0 && range !== null) {
      current.chart
        .timeScale()
        .setVisibleLogicalRange({ from: range.from + added, to: range.to + added });
    }

    shown.current = { first, count: candles.length };
  }, [candles]);

  // The live candle goes into its place without drawing the others again.
  useEffect(() => {
    if (live !== null && drawn.current !== null && candles.length > 0) {
      drawn.current.candles.update({
        time: seconds(live.openTime),
        open: Number(live.open),
        high: Number(live.high),
        low: Number(live.low),
        close: Number(live.close),
      });
    }
  }, [live, candles.length]);

  useEffect(() => {
    drawn.current?.interest.setData(
      interest.map((point) => ({ time: seconds(point.time), value: Number(point.value) })),
    );
  }, [interest]);

  useEffect(() => {
    drawn.current?.funding.setData(
      funding.map((point) => {
        const rate = Number(point.value);
        return {
          time: seconds(point.time),
          value: rate,
          color:
            Math.abs(rate) >= highPayment
              ? chartColors.selected
              : rate < 0
                ? chartColors.down
                : chartColors.up,
        };
      }),
    );
  }, [funding]);

  useEffect(() => {
    drawn.current?.markers.setMarkers(
      [...marks]
        .sort((a, b) => a.time - b.time)
        .map((mark) => ({
          time: seconds(mark.time),
          position: mark.side === 'short' ? ('aboveBar' as const) : ('belowBar' as const),
          shape:
            mark.kind === 'exit'
              ? ('circle' as const)
              : mark.side === 'short'
                ? ('arrowDown' as const)
                : ('arrowUp' as const),
          color:
            mark.id === selected
              ? chartColors.selected
              : mark.kind === 'exit'
                ? chartColors.exit
                : chartColors[mark.side === 'short' ? 'short' : 'long'],
          text: mark.text,
        })),
    );
  }, [marks, selected, candles]);

  // The lines of the open ideas (task 18.26): the entry, the stop and the target, each a line from the candle of the entry
  // to the candle of the term, and the term marked at the end of the line of the entry.
  useEffect(() => {
    const current = drawn.current;
    if (current === null) {
      return;
    }

    const made: ISeriesApi<'Line'>[] = [];
    for (const line of lines) {
      const series = current.chart.addSeries(LineSeries, {
        title: line.title,
        color: lineColors[line.kind],
        lineWidth: 1,
        lineStyle: LineStyle.Dashed,
        lastValueVisible: true,
        priceLineVisible: false,
        crosshairMarkerVisible: false,
      });
      const points = [{ time: seconds(line.from), value: line.price }];
      if (line.to > line.from) {
        points.push({ time: seconds(line.to), value: line.price });
      }

      series.setData(points);
      if (line.term !== undefined) {
        createSeriesMarkers(series, [
          {
            time: seconds(line.to),
            position: 'inBar',
            shape: 'square',
            color: chartColors.text,
            text: line.term,
          },
        ]);
      }

      made.push(series);
    }

    return () => made.forEach((series) => current.chart.removeSeries(series));
  }, [lines, candles]);

  return <div ref={container} className="h-[600px] w-full" data-testid="chart" />;
}
