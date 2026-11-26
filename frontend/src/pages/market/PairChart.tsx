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
import { crosshairTime, timeTick } from '@/preview/chartFormat';

import { LineLabels } from './lineLabels';
import {
  chartHeight,
  type IdeaLine,
  type IdeaMark,
  type LineLabel,
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
  /** The empty points of the time scale after the last candle up to the term of the open ideas (task 20.2). */
  readonly slots: ISeriesApi<'Line'>;
  readonly markers: ISeriesMarkersPluginApi<Time>;
}

/** A line of an open idea on the chart: its series and the labels written at its end. */
interface MadeLine {
  readonly series: ISeriesApi<'Line'>;
  labels: LineLabels;
}

/**
 * The lines of the open ideas drawn on a chart: which lines they are (`set`, the idea and the kind of each), their points
 * and labels as last given (`key`), and their series (task 21.1).
 */
interface Made {
  readonly chart: IChartApi;
  readonly set: string;
  key: string;
  readonly lines: readonly MadeLine[];
}

/**
 * The last candle the lines of the open ideas were laid out after, and where the right edge of the range in view is meant
 * to stand (task 23.1): `distance` bars past that candle, `bars` in view. The library applies a right edge asked for on
 * the next frame, so while one `waits` the range it shows is not the one it will show. `seen` is the distance the library
 * itself showed after the chart was last given data or drew what it was asked: a range moved since is the user's.
 */
interface Laid {
  readonly chart: IChartApi;
  readonly last: number | string | undefined;
  distance: number | null;
  bars: number;
  waits: boolean;
  seen: number | null;
}

const seconds = (ms: number | string) => (Number(ms) / 1000) as UTCTimestamp;

/** How far the right edge of the range in view stands past the last candle laid out, in bars, when it can be told. */
function distanceOf(chart: IChartApi, laid: Laid): { distance: number; bars: number } | null {
  const scale = chart.timeScale();
  const view = scale.getVisibleLogicalRange();
  const index = laid.last === undefined ? null : scale.timeToIndex(seconds(laid.last), false);
  return view === null || index === null
    ? null
    : { distance: view.to - index, bars: view.to - view.from };
}

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
  // The room to the right of the last candle the time scale of the chart was last given (task 20.1).
  const offset = useRef<{ chart: IChartApi; value: number } | null>(null);
  // The lines drawn and the slots given: drawn again only when they change, not on every candle (task 20.2).
  const made = useRef<Made | null>(null);
  const slotsGiven = useRef<{ chart: IChartApi; key: string } | null>(null);
  // The last candle the lines were laid out after and how far past it the right edge is meant to stand: the candles closed
  // since move the range in view with them when that candle was in it (tasks 22.1, 23.1).
  const laidOut = useRef<Laid | null>(null);

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
      // The scale and the crosshair in the zone of the browser (task 3.4): the data stay in seconds of UTC.
      localization: { locale: 'ru-RU', timeFormatter: crosshairTime },
      timeScale: { timeVisible: true, secondsVisible: false, tickMarkFormatter: timeTick },
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
    // Nothing to draw: only the empty points of the time scale the lines of the open ideas end on (task 20.2).
    const slotsSeries = chart.addSeries(LineSeries, {
      visible: false,
      lastValueVisible: false,
      priceLineVisible: false,
    });
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
      slots: slotsSeries,
      markers: createSeriesMarkers(series, []),
    };

    return () => {
      drawn.current = null;
      chart.unsubscribeClick(click);
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(scroll);
      chart.remove();
    };
  }, []);

  // Before this render gives the chart anything (task 23.1): a range in view moved since the library last showed one is
  // the user's, who scrolled or zoomed, and the right edge is meant to stand where they left it. A range the library moved
  // itself on new data is not: when candles go past the end of the lines it follows them, and the lines follow them too.
  useEffect(() => {
    const current = drawn.current;
    const laid = laidOut.current;
    if (current === null || laid === null || laid.chart !== current.chart || laid.waits) {
      return;
    }

    const now = distanceOf(current.chart, laid);
    if (now === null) {
      return;
    }

    laid.distance =
      laid.distance === null || laid.seen === null
        ? now.distance
        : laid.distance + now.distance - laid.seen;
    laid.bars = now.bars;
    laid.seen = now.distance;
  });

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
  // and room for them to the right of the last candle (task 19.14). The end of the lines stands on the empty slot of its
  // term after the last candle (task 20.2). Lines and slots that did not change are not given again: every series taken
  // off or added moves the last point of the time scale, and the range in view with it. The series of the same lines are
  // kept and given their new points (task 21.1): the end of a line cut by a third of the bars in view moves with every
  // closed candle and every change of the zoom. The library keeps the range in view when the last bar moves right onto a
  // slot that was there, and when it moves left (a line made shorter, a line taken off) it takes the range with it, so
  // the range in view before the change is given back after it: where it was when the last candle was out of view, moved
  // by the candles closed since when it was in view, so the chart follows the live candle as it does without lines (task
  // 22.1). It is given back as the right edge, not as the range: the library applies both on the next frame, where a
  // range keeps the number of bars in view and a price scale grown or shrunk by a new price changes the spacing of the
  // bars and moves them; the right edge keeps the spacing as the library does without lines. Where it stands is counted
  // from the last candle, not read from the range (task 23.1): in a tab in the background the edge asked for at one candle
  // still waits when the next one closes, and candles past the end of the lines have already moved the range.
  useEffect(() => {
    const current = drawn.current;
    if (current === null) {
      return;
    }

    const scale = current.chart.timeScale();
    const view = scale.getVisibleLogicalRange();
    const lastTime = candles.at(-1)?.openTime;
    const room = rightRoom(
      lines,
      lastTime === undefined ? undefined : Number(lastTime),
      timeframe,
      view === null ? 150 : view.to - view.from,
      scale.options().barSpacing,
    );
    // Where the right edge is meant to stand before lines and slots change (task 23.1): past the last candle laid out by the
    // distance it had, not by the range in view, which the library has not applied yet while a right edge asked for waits
    // for a frame, and which it has already moved itself when the candles went past the end of the lines. The chart follows
    // the candles when the last candle laid out was in the range meant, the right edge as far past the new last candle.
    const last = lastTime === undefined ? null : scale.timeToIndex(seconds(lastTime), true);
    const laid = laidOut.current?.chart === current.chart ? laidOut.current : null;
    const laidIndex =
      laid?.last === undefined ? null : scale.timeToIndex(seconds(laid.last), false);
    const meant =
      laid !== null && laid.distance !== null && laidIndex !== null
        ? { last: laidIndex, to: laidIndex + laid.distance, bars: laid.bars }
        : view !== null && last !== null
          ? { last, to: view.to, bars: view.to - view.from }
          : null;
    const follows = meant !== null && meant.to - meant.bars <= meant.last && meant.last <= meant.to;
    let edge =
      meant === null ? null : follows && last !== null ? last + meant.to - meant.last : meant.to;

    let changed = false;
    const slotsKey = room.slots.join(',');
    if (slotsGiven.current?.chart !== current.chart || slotsGiven.current.key !== slotsKey) {
      current.slots.setData(room.slots.map((time) => ({ time: seconds(time) })));
      slotsGiven.current = { chart: current.chart, key: slotsKey };
      changed = true;
    }

    const linesKey = JSON.stringify(room.lines);
    const linesSet = room.lines.map((line) => `${line.id}:${line.kind}`).join(',');
    const before = made.current?.chart === current.chart ? made.current : null;
    const setChanged = before === null || before.set !== linesSet;
    if (setChanged) {
      before?.lines.forEach((line) => current.chart.removeSeries(line.series));
      made.current = {
        chart: current.chart,
        set: linesSet,
        key: linesKey,
        lines: drawLines(current.chart, room.lines),
      };
      changed = true;
    } else if (before.key !== linesKey) {
      redrawLines(before.lines, room.lines);
      before.key = linesKey;
      changed = true;
    }

    // The last bar of the time scale now: the right edge less the room past it. A right edge still waiting for a frame is
    // asked for again, data given meanwhile having moved the last bar under it.
    const now = scale.getVisibleLogicalRange();
    const base = now === null ? null : now.to - scale.scrollPosition();
    let asked = false;
    if ((changed || laid?.waits === true) && edge !== null && base !== null) {
      scale.scrollToPosition(edge - base, false);
      asked = true;
    }

    // The library scrolls to the right edge on every `rightOffset` it is given (task 20.1): a chart gets one when it has its
    // first candles, and later only when the lines came or went, the room changed and the last candle was in view — a page
    // loaded before, a closed candle or a change of the zoom leaves the range where it is (task 21.1). The last candle in
    // view before lines and slots changed, not the offset from the last bar: lines and slots added after the candles move
    // the last bar of the time scale (task 20.2), and the range the library shows right after a change is not the one it
    // shows on the next frame. The chart is compared too: under `StrictMode` it is drawn again with the refs kept.
    const given = offset.current?.chart === current.chart ? offset.current.value : null;
    if (lastTime !== undefined && room.offset !== given && (given === null || setChanged)) {
      if (given === null || meant === null || follows) {
        scale.applyOptions({ rightOffset: room.offset });
        offset.current = { chart: current.chart, value: room.offset };
        edge = base === null ? null : base + room.offset;
        asked = true;
      }
    }

    const record: Laid = {
      chart: current.chart,
      last: lastTime,
      distance: edge === null || last === null ? null : edge - last,
      bars: meant?.bars ?? (view === null ? 0 : view.to - view.from),
      waits: asked,
      seen: null,
    };
    laidOut.current = record;
    if (asked) {
      // Registered after the library's own frame, so it runs once the library drew what it was asked.
      requestAnimationFrame(() => {
        record.waits = false;
        const shown =
          drawn.current?.chart === record.chart ? distanceOf(record.chart, record) : null;
        record.seen = shown?.distance ?? null;
      });
    }
  }, [lines, candles, timeframe]);

  // After this render gave the chart its data (task 23.1): where the library itself shows the right edge, so a range moved
  // before the next render is told from one the library moved on the data given.
  useEffect(() => {
    const current = drawn.current;
    const laid = laidOut.current;
    if (current === null || laid === null || laid.chart !== current.chart || laid.waits) {
      return;
    }

    const now = distanceOf(current.chart, laid);
    laid.seen = now?.distance ?? null;
    if (now !== null) {
      laid.bars = now.bars;
    }
  });

  return (
    <div ref={container} className="w-full" style={{ height: chartHeight }} data-testid="chart" />
  );
}

/** The points of a line: from the candle of the entry to the slot of its end, one point when they are the same. */
function pointsOf(line: IdeaLine): { time: UTCTimestamp; value: number }[] {
  const points = [{ time: seconds(line.from), value: line.price }];
  if (line.to > line.from) {
    points.push({ time: seconds(line.to), value: line.price });
  }

  return points;
}

/** The labels a line writes at its end, of the labels of all the lines: its own, and the term at the end of the entry. */
function labelsOf(line: IdeaLine, labels: readonly LineLabel[]): LineLabels {
  return new LineLabels(
    line.title,
    labels.filter(
      (label) =>
        label.id === line.id &&
        (label.kind === line.kind || (label.kind === 'term' && line.kind === 'entry')),
    ),
    line.kind === 'entry' ? chartColors.text : lineColors[line.kind],
    labels,
  );
}

/**
 * The lines of the open ideas on the chart, each with the labels at its end: their places are laid out over the labels of
 * all the lines, so those of close levels move apart (task 20.2). The chart removed takes its lines with it (task 19.1).
 */
function drawLines(chart: IChartApi, lines: readonly IdeaLine[]): MadeLine[] {
  const labels = lineLabels(lines);
  return lines.map((line) => {
    const series = chart.addSeries(LineSeries, {
      color: lineColors[line.kind],
      lineWidth: 1,
      lineStyle: LineStyle.Dashed,
      lastValueVisible: false,
      priceLineVisible: false,
      crosshairMarkerVisible: false,
    });
    series.setData(pointsOf(line));
    const written = labelsOf(line, labels);
    series.attachPrimitive(written);
    return { series, labels: written };
  });
}

/**
 * The same lines given their new points and labels (task 21.1): `made` and `lines` are of the same ideas and kinds in
 * the same order. The series stay on the chart; only their data and the labels at their ends change.
 */
function redrawLines(made: readonly MadeLine[], lines: readonly IdeaLine[]): void {
  const labels = lineLabels(lines);
  made.forEach((drawnLine, index) => {
    const line = lines[index]!;
    drawnLine.series.setData(pointsOf(line));
    const written = labelsOf(line, labels);
    drawnLine.series.detachPrimitive(drawnLine.labels);
    drawnLine.series.attachPrimitive(written);
    drawnLine.labels = written;
  });
}
