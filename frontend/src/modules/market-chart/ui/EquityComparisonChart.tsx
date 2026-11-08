import {
  ColorType,
  createChart,
  type IChartApi,
  LineSeries,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';

import { drawableEquity } from '../model/pricePoint';
import { localTime } from './chartTime';
import { token } from './tokens';

export interface EquityLine {
  readonly label: string;
  /** A theme token, such as `--color-series-1`. */
  readonly color: string;
  readonly points: readonly { openTime: string; equity: string }[];
}

/** The equity of several results on one time scale in the local zone, a colored line for each. */
export function EquityComparisonChart({
  lines,
  label,
}: {
  lines: readonly EquityLine[];
  label: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<IChartApi | null>(null);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

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
      ...localTime,
    });
    chart.current = api;

    return () => {
      api.remove();
      chart.current = null;
    };
  }, []);

  // The lines arrive as a new array on every render; they are drawn again only when what they show changes.
  const latest = useRef(lines);
  latest.current = lines;
  const signature = lines
    .map(
      (line) => `${line.label}:${line.color}:${line.points.length}:${line.points.at(-1)?.openTime}`,
    )
    .join('|');

  useEffect(() => {
    const api = chart.current;

    if (!api) {
      return undefined;
    }

    const series = latest.current.map((line) => {
      const added = api.addSeries(LineSeries, {
        color: token(line.color, '#7aa7ff'),
        lineWidth: 2,
        title: line.label,
      });
      added.setData(
        drawableEquity(line.points).map((value) => ({
          ...value,
          time: value.time as UTCTimestamp,
        })),
      );

      return added;
    });
    api.timeScale().fitContent();

    return () => {
      // When the page goes away the chart is removed first and takes its series with it.
      if (chart.current === api) {
        series.forEach((added) => api.removeSeries(added));
      }
    };
  }, [signature]);

  return (
    <figure
      aria-label={label}
      data-lines={lines.length}
      className="m-0 h-80 w-full overflow-hidden rounded-md"
    >
      <div ref={container} className="size-full" />
    </figure>
  );
}
