import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import type { PreviewCandle } from '@/api/client';

import type { EventMark } from './preview';

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
 * `focus` shows that part of the chart, as a click on an event asks.
 */
export function CandleChart({
  candles,
  marks,
  focus,
  heightClass = 'h-[480px]',
}: {
  readonly candles: readonly PreviewCandle[];
  readonly marks: readonly EventMark[];
  readonly focus?: ChartRange | null;
  readonly heightClass?: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<Drawn | null>(null);

  useEffect(() => {
    if (container.current === null) {
      return;
    }

    const chart = createChart(container.current, {
      autoSize: true,
      layout: { background: { type: ColorType.Solid, color: '#09090b' }, textColor: '#a1a1aa' },
      grid: { vertLines: { color: '#18181b' }, horzLines: { color: '#18181b' } },
      timeScale: { timeVisible: true, secondsVisible: false },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderVisible: false,
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });
    drawn.current = { chart, candles: series, markers: createSeriesMarkers(series, []) };

    return () => {
      drawn.current = null;
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
    current.markers.setMarkers(
      marks.map((mark) => ({
        time: mark.time as UTCTimestamp,
        position: mark.position ?? ('aboveBar' as const),
        shape: mark.shape ?? ('arrowDown' as const),
        color: mark.color ?? '#facc15',
        text: mark.text ?? '⚡',
      })),
    );
    current.chart.timeScale().fitContent();
  }, [candles, marks]);

  useEffect(() => {
    if (focus && drawn.current) {
      drawn.current.chart
        .timeScale()
        .setVisibleRange({ from: focus.from as UTCTimestamp, to: focus.to as UTCTimestamp });
    }
  }, [focus]);

  return <div ref={container} className={`${heightClass} w-full`} data-testid="chart" />;
}
