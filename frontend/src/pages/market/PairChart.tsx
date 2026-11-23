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

import { LineLabels } from './lineLabels';
import {
  chartHeight,
  type IdeaLine,
  type IdeaMark,
  lineLabels,
  minMoveOf,
  panelMargins,
  paneStretch,
  rightRoom,
  scaleAmount,
  scalePrice,
  scaleRate,
} from './pairSeries';

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
  timeframe,
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
  /** The timeframe of the candles: the room to the right of the last one is counted in its bars. */
  readonly timeframe: string;
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
      // No price formatter for the whole chart: it would stand over the formats of the series (task 19.14).
      localization: { locale: 'ru-RU' },
      timeScale: { timeVisible: true, secondsVisible: false },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: chartColors.up,
      downColor: chartColors.down,
      borderVisible: false,
      wickUpColor: chartColors.up,
      wickDownColor: chartColors.down,
      priceFormat: { type: 'custom', formatter: scalePrice, minMove: minMoveOf(undefined) },
    });
    const interestSeries = chart.addSeries(
      LineSeries,
      {
        color: interestColor,
        lineWidth: 2,
        priceFormat: { type: 'custom', formatter: scaleAmount },
      },
      1,
    );
    const fundingSeries = chart.addSeries(
      HistogramSeries,
      { priceFormat: { type: 'custom', formatter: scaleRate, minMove: 0.000001 } },
      2,
    );
    // The panels about 90 pixels each, as in the mockup, their marks off the edges they share (task 19.14).
    chart
      .panes()
      .forEach((pane, index) =>
        pane.setStretchFactor(index === 0 ? paneStretch.candles : paneStretch.panel),
      );
    interestSeries.priceScale().applyOptions({ scaleMargins: panelMargins });
    fundingSeries.priceScale().applyOptions({ scaleMargins: panelMargins });

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
    const last = candles.at(-1);
    current.candles.applyOptions({
      priceFormat: {
        type: 'custom',
        formatter: scalePrice,
        minMove: minMoveOf(last === undefined ? undefined : Number(last.close)),
      },
    });
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
  // to the candle of the term, no further than a third of the bars in view, with their labels to the right of their ends
  // and room for them to the right of the last candle (task 19.14).
  useEffect(() => {
    const current = drawn.current;
    if (current === null) {
      return;
    }

    const scale = current.chart.timeScale();
    const view = scale.getVisibleLogicalRange();
    const lastOpen = candles.at(-1)?.openTime;
    const room = rightRoom(
      lines,
      lastOpen === undefined ? undefined : Number(lastOpen),
      timeframe,
      view === null ? 150 : view.to - view.from,
      scale.options().barSpacing,
    );
    scale.applyOptions({ rightOffset: room.offset });
    const labels = lineLabels(room.lines);
    const made: ISeriesApi<'Line'>[] = [];
    for (const line of room.lines) {
      const series = current.chart.addSeries(LineSeries, {
        color: lineColors[line.kind],
        lineWidth: 1,
        lineStyle: LineStyle.Dashed,
        lastValueVisible: false,
        priceLineVisible: false,
        crosshairMarkerVisible: false,
      });
      const points = [{ time: seconds(line.from), value: line.price }];
      if (line.to > line.from) {
        points.push({ time: seconds(line.to), value: line.price });
      }

      series.setData(points);
      series.attachPrimitive(
        new LineLabels(
          line.title,
          labels.filter(
            (label) =>
              label.id === line.id &&
              (label.kind === line.kind || (label.kind === 'term' && line.kind === 'entry')),
          ),
          line.kind === 'entry' ? chartColors.text : lineColors[line.kind],
        ),
      );
      made.push(series);
    }

    // On unmount the chart is removed first, by the cleanup above, with its series; the library throws on a call to a
    // removed chart (task 19.1), so only a chart still drawn has its lines taken off.
    return () => {
      if (drawn.current === current) {
        made.forEach((series) => current.chart.removeSeries(series));
      }
    };
  }, [lines, candles, timeframe]);

  return (
    <div ref={container} className="w-full" style={{ height: chartHeight }} data-testid="chart" />
  );
}
