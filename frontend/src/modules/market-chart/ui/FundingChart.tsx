import { HistogramSeries, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts';
import { useEffect, useMemo, useRef } from 'react';

import { fundingBars, type FundingInput } from '../model/pairSeries';
import { createPairChart } from './pairChart';
import { token } from './tokens';

/** The funding settlements of a linear pair in percent as columns, green above zero and red below. */
export function FundingChart({
  points,
  label,
}: {
  points: readonly FundingInput[];
  label: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const drawn = useRef<{
    series: ISeriesApi<'Histogram'>;
    fit: () => void;
  } | null>(null);
  const bars = useMemo(() => fundingBars(points), [points]);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const api = createPairChart(container.current);
    drawn.current = {
      // Rates in percent are hundredths: the axis needs four decimals.
      series: api.addSeries(HistogramSeries, {
        priceFormat: { type: 'price', precision: 4, minMove: 0.0001 },
      }),
      fit: () => api.timeScale().fitContent(),
    };

    return () => {
      api.remove();
      drawn.current = null;
    };
  }, []);

  useEffect(() => {
    const up = token('--color-ok', '#3fb37f');
    const down = token('--color-err', '#e5484d');

    drawn.current?.series.setData(
      bars.map((bar) => ({
        time: bar.time as UTCTimestamp,
        value: bar.value,
        color: bar.positive ? up : down,
      })),
    );
    drawn.current?.fit();
  }, [bars]);

  return (
    <figure role="img" aria-label={label} className="m-0 h-50 w-full overflow-hidden rounded-md">
      <div ref={container} className="size-full" />
    </figure>
  );
}
