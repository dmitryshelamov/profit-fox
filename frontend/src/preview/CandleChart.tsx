import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  TickMarkType,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import type { PreviewCandle } from '@/api/client';

import { monthTick, priceText } from './chartFormat';
import { type EventMark, keyOf } from './preview';

/**
 * The colors of the chart. The chart draws on a canvas and takes its colors as values, not as classes, so the tokens
 * of the theme are repeated here in one table (change redesign-bench-ui, design.md, decision 2): `canvas`, `panel`,
 * `muted`, `ok`, `err`, `pin-event`, `text` and `accent`.
 */
export const chartColors = {
  background: '#101216',
  grid: '#1B1E25',
  text: '#8C94A3',
  up: '#3FB37F',
  down: '#E5484D',
  /** The mark of an event of a signal. */
  event: '#F5B84A',
  /** The entry of a trade by its side and its exit. */
  short: '#E5484D',
  long: '#3FB37F',
  exit: '#E7E9EE',
  /** The marks of the selected event or trade. */
  selected: '#F28C28',
} as const;

interface Drawn {
  readonly chart: IChartApi;
  readonly candles: ISeriesApi<'Candlestick'>;
  readonly markers: ISeriesMarkersPluginApi<Time>;
}

/** A part of the chart to show: from and to in seconds, as the chart counts time. */
export interface ChartRange {
  readonly from: number;
  readonly to: number;
}

/**
 * The candles of the pair with a mark on each candle that closed at an event; the chart counts time in UTC. A
 * `focus` shows that part of the chart, as a click on an event asks. A click on a candle that has a mark gives the
 * mark to `onPick` — the entry of a trade when a candle has both an entry and an exit — and the marks of `selected`
 * are drawn in the color of the choice; a click on a candle without a mark does nothing.
 */
export function CandleChart({
  candles,
  marks,
  focus,
  selected = null,
  onPick,
  heightClass = 'h-[480px]',
}: {
  readonly candles: readonly PreviewCandle[];
  readonly marks: readonly EventMark[];
  readonly focus?: ChartRange | null;
  /** The key of the selected event or trade, as `keyOf` gives it. */
  readonly selected?: number | null;
  readonly onPick?: (mark: EventMark) => void;
  readonly heightClass?: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<Drawn | null>(null);
  // The click handler of the chart is set once; it reads the marks and the listener of the last render.
  const latest = useRef({ marks, onPick });
  latest.current = { marks, onPick };

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
      timeScale: {
        timeVisible: true,
        secondsVisible: false,
        // The whole history of a pair fits the panel, so every mark of a preview is in view at once: three years of
        // hourly candles are 26 thousand bars on a thousand pixels.
        minBarSpacing: 0.02,
        tickMarkFormatter: (time: Time, type: TickMarkType) =>
          type === TickMarkType.Month ? monthTick(Number(time)) : null,
      },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: chartColors.up,
      downColor: chartColors.down,
      borderVisible: false,
      wickUpColor: chartColors.up,
      wickDownColor: chartColors.down,
    });
    const click = (param: { time?: Time }) => {
      const onCandle = latest.current.marks.filter((mark) => mark.time === param.time);
      const mark = onCandle.find((candidate) => candidate.tone !== 'exit') ?? onCandle[0];
      if (mark !== undefined) {
        latest.current.onPick?.(mark);
      }
    };
    chart.subscribeClick(click);
    drawn.current = { chart, candles: series, markers: createSeriesMarkers(series, []) };

    return () => {
      drawn.current = null;
      chart.unsubscribeClick(click);
      chart.remove();
    };
  }, []);

  useEffect(() => {
    const current = drawn.current;
    if (current === null) {
      return;
    }

    current.candles.setData(
      candles.map((candle) => ({
        time: (Number(candle.openTime) / 1000) as UTCTimestamp,
        open: Number(candle.open),
        high: Number(candle.high),
        low: Number(candle.low),
        close: Number(candle.close),
      })),
    );
    current.chart.timeScale().fitContent();
  }, [candles]);

  // A new selection only recolors the marks: the part of the chart in view stays.
  useEffect(() => {
    drawn.current?.markers.setMarkers(
      marks.map((mark) => ({
        time: mark.time as UTCTimestamp,
        position: mark.position ?? ('aboveBar' as const),
        shape: mark.shape ?? ('arrowDown' as const),
        color:
          selected !== null && keyOf(mark) === selected
            ? chartColors.selected
            : chartColors[mark.tone ?? 'event'],
        text: mark.text ?? '⚡',
      })),
    );
  }, [candles, marks, selected]);

  useEffect(() => {
    if (focus && drawn.current) {
      drawn.current.chart
        .timeScale()
        .setVisibleRange({ from: focus.from as UTCTimestamp, to: focus.to as UTCTimestamp });
    }
  }, [focus]);

  return <div ref={container} className={`${heightClass} w-full`} data-testid="chart" />;
}
