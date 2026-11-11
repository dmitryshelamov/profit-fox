import {
  HistogramSeries,
  type ISeriesApi,
  LineSeries,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useMemo, useRef } from 'react';

import { type TradeFlowInput, tradeFlowSeries } from '../model/tradeFlowSeries';
import { createPairChart } from './pairChart';
import { token } from './tokens';

/**
 * The panel «Покупки и продажи»: the buys of each bar in USDT as a column up, the sells as a column down, and the line of CVD over them; the
 * current bar `current` is drawn fainter. The figure describes it, as the canvas gives no text.
 */
export function TradeFlowChart({
  bars,
  current,
  label,
  formatValue,
}: {
  bars: readonly TradeFlowInput[];
  current: string | null;
  label: string;
  formatValue: (value: number) => string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<{
    buys: ISeriesApi<'Histogram'>;
    sells: ISeriesApi<'Histogram'>;
    cvd: ISeriesApi<'Line'>;
    fit: () => void;
  } | null>(null);
  const series = useMemo(() => tradeFlowSeries(bars), [bars]);
  const format = useRef(formatValue);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const api = createPairChart(container.current, (value) => format.current(value));
    drawn.current = {
      buys: api.addSeries(HistogramSeries, { color: token('--color-ok', '#3fb37f') }),
      sells: api.addSeries(HistogramSeries, { color: token('--color-err', '#e5484d') }),
      cvd: api.addSeries(LineSeries, { color: token('--color-series-1', '#7aa7ff'), lineWidth: 2 }),
      fit: () => api.timeScale().fitContent(),
    };

    return () => {
      api.remove();
      drawn.current = null;
    };
  }, []);

  useEffect(() => {
    const at = current === null ? null : Date.parse(current) / 1000;
    const faint = token('--color-muted', '#8c94a3');
    const point = (item: { time: number; value: number }) => ({
      time: item.time as UTCTimestamp,
      value: item.value,
      ...(item.time === at ? { color: faint } : {}),
    });

    drawn.current?.buys.setData(series.buys.map(point));
    drawn.current?.sells.setData(series.sells.map(point));
    drawn.current?.cvd.setData(
      series.cvd.map((item) => ({ ...item, time: item.time as UTCTimestamp })),
    );
    drawn.current?.fit();
  }, [series, current]);

  return (
    <figure role="img" aria-label={label} className="m-0 h-50 w-full overflow-hidden rounded-md">
      <div ref={container} className="size-full" />
    </figure>
  );
}
