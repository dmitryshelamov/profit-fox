import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  LineSeries,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import { drawable, type FillMarker, type PricePoint } from '../model/pricePoint';
import { seriesMarkers } from '../model/seriesMarkers';
import { localTime } from './chartTime';
import { token } from './tokens';

/** Price candles on the right scale, account equity on the left, fills marked on the candles; time in the local zone. */
export function PriceEquityChart({
  points,
  markers,
  label,
}: {
  points: readonly PricePoint[];
  markers: readonly FillMarker[];
  label: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<{
    api: IChartApi;
    candles: ISeriesApi<'Candlestick'>;
    equity: ISeriesApi<'Line'>;
    marks: ISeriesMarkersPluginApi<Time>;
  } | null>(null);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const up = token('--color-ok', '#3fb37f');
    const down = token('--color-err', '#e5484d');
    const api = createChart(container.current, {
      height: 320,
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: token('--color-canvas', '#101216') },
        textColor: token('--color-muted', '#8c94a3'),
      },
      grid: {
        vertLines: { color: token('--color-line', '#2c313b') },
        horzLines: { color: token('--color-line', '#2c313b') },
      },
      leftPriceScale: { visible: true },
      ...localTime,
    });
    const candles = api.addSeries(CandlestickSeries, {
      upColor: up,
      downColor: down,
      wickUpColor: up,
      wickDownColor: down,
      borderVisible: false,
    });
    chart.current = {
      api,
      candles,
      marks: createSeriesMarkers(candles, []),
      equity: api.addSeries(LineSeries, {
        color: token('--color-run', '#7aa7ff'),
        lineWidth: 2,
        priceScaleId: 'left',
        title: 'Капитал',
      }),
    };

    return () => {
      api.remove();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    const drawn = drawable(points);

    chart.current?.candles.setData(
      drawn.candles.map((candle) => ({ ...candle, time: candle.time as UTCTimestamp })),
    );
    chart.current?.equity.setData(
      drawn.equity.map((value) => ({ ...value, time: value.time as UTCTimestamp })),
    );
    chart.current?.api.timeScale().fitContent();
  }, [points]);

  useEffect(() => {
    if (!chart.current) {
      return;
    }

    const colors = { entry: token('--color-ok', '#3fb37f'), exit: token('--color-err', '#e5484d') };

    chart.current.marks.setMarkers(
      seriesMarkers(markers).map(({ kind, time, ...marker }) => ({
        ...marker,
        time: time as UTCTimestamp,
        color: colors[kind],
      })),
    );
  }, [markers]);

  return (
    <figure
      aria-label={label}
      data-points={points.length}
      data-entries={markers.filter((marker) => marker.kind === 'entry').length}
      data-exits={markers.filter((marker) => marker.kind === 'exit').length}
      className="m-0 h-80 w-full overflow-hidden rounded-md"
    >
      <div ref={container} className="size-full" />
    </figure>
  );
}
