import { type ISeriesApi, LineSeries, type UTCTimestamp } from 'lightweight-charts';
import { useEffect, useMemo, useRef } from 'react';

import { type OpenInterestInput, openInterestLine } from '../model/pairSeries';
import { createPairChart } from './pairChart';
import { token } from './tokens';

/** The open interest of a linear pair in USDT as a line. The figure describes it, as the canvas gives no text. */
export function OpenInterestChart({
  points,
  label,
  formatValue,
}: {
  points: readonly OpenInterestInput[];
  label: string;
  /** Writes a value of the axis. */
  formatValue: (value: number) => string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<{
    series: ISeriesApi<'Line'>;
    fit: () => void;
  } | null>(null);
  const line = useMemo(() => openInterestLine(points), [points]);
  const format = useRef(formatValue);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const api = createPairChart(container.current, (value) => format.current(value));
    drawn.current = {
      series: api.addSeries(LineSeries, {
        color: token('--color-series-1', '#7aa7ff'),
        lineWidth: 2,
      }),
      fit: () => api.timeScale().fitContent(),
    };

    return () => {
      api.remove();
      drawn.current = null;
    };
  }, []);

  useEffect(() => {
    drawn.current?.series.setData(
      line.map((point) => ({ ...point, time: point.time as UTCTimestamp })),
    );
    drawn.current?.fit();
  }, [line]);

  return (
    <figure role="img" aria-label={label} className="m-0 h-50 w-full overflow-hidden rounded-md">
      <div ref={container} className="size-full" />
    </figure>
  );
}
