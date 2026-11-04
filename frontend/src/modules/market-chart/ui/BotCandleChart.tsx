import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  HistogramSeries,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type LogicalRange,
  type MouseEventParams,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useMemo, useRef } from 'react';

import { candleEnd } from '../model/chartPaging';
import {
  type Bar,
  type BarInput,
  drawnBars,
  type LiveCandle,
  liveCandles,
} from '../model/liveCandles';
import { type OrderInput, orderLines } from '../model/orderLines';
import { type FillInput, tradeMarkers } from '../model/tradeMarkers';
import { type VolumeBar, volumeBars } from '../model/volumeBars';
import { token, withAlpha } from './tokens';

/** How many candles the chart shows when it first opens. */
const initialBars = 120;

/** How close to the oldest loaded candle the view must come before older candles are asked for. */
const loadAheadBars = 10;

const nothing: never[] = [];

const formingAlpha = 0.45;
const volumeAlpha = 0.5;
const formingVolumeAlpha = 0.25;
/** `LineStyle.Dashed`. */
const dashed = 2;

const colors = () => ({
  up: token('--color-ok', '#3fb37f'),
  down: token('--color-err', '#e5484d'),
});

const candleOf = (bar: Bar) => ({
  time: bar.time as UTCTimestamp,
  open: bar.open,
  high: bar.high,
  low: bar.low,
  close: bar.close,
});

const volumeColumn = (bar: VolumeBar) => ({
  time: bar.time as UTCTimestamp,
  value: bar.value,
  color: withAlpha(
    bar.rising ? colors().up : colors().down,
    bar.forming ? formingVolumeAlpha : volumeAlpha,
  ),
});

/** The candle that is not closed yet is drawn see-through. */
function formingStyle(bar: Bar) {
  if (!bar.forming) {
    return {};
  }

  const color = withAlpha(bar.close >= bar.open ? colors().up : colors().down, formingAlpha);

  return { color, wickColor: color, borderColor: color };
}

/**
 * Closed candles in UTC with their volume on a hidden scale under them, the bot's fills as arrows whose tips stand on
 * the price, and its active limit orders as dashed lines. The current candle follows the closed ones, see-through, and is
 * drawn with `update`. Scrolling near the oldest loaded candle calls `onReachStart`; candles added before it keep the view
 * where it was. The figure describes what is drawn, as the canvas gives no text.
 */
export function BotCandleChart({
  history,
  live = nothing,
  fills,
  orders,
  interval,
  label,
  onReachStart,
  onHover,
}: {
  history: readonly BarInput[];
  /** The current candle, and the one before it until the history has it; the history wins over a repeated one. */
  live?: readonly LiveCandle[];
  /** Without fills and orders, as on the page of a pair, the chart is described by its label alone. */
  fills?: readonly FillInput[];
  orders?: readonly OrderInput[];
  /** The Bybit interval of the candles, which says where each one ends. */
  interval: string;
  label: string;
  onReachStart: () => void;
  /** Tells the open time in seconds of the candle under the cursor, or `null` once the cursor leaves the candles. */
  onHover?: (time: number | null) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<{
    api: IChartApi;
    candles: ISeriesApi<'Candlestick'>;
    volume: ISeriesApi<'Histogram'>;
    markers: ISeriesMarkersPluginApi<Time>;
    lines: IPriceLine[];
  } | null>(null);
  const firstTime = useRef<number | null>(null);
  const drawnHistory = useRef<unknown>(null);
  const reachStart = useRef(onReachStart);
  const hover = useRef(onHover);
  /** True while the chart takes in data: the view it announces then is not the reader's and must not page back. */
  const applying = useRef(false);
  const closed = useMemo(() => drawnBars(history, false), [history]);
  const current = useMemo(
    () => liveCandles(history, live).flatMap((candle) => drawnBars([candle], !candle.isClosed)),
    [history, live],
  );
  const markers = useMemo(
    () =>
      tradeMarkers([...closed, ...current], fills ?? nothing, (time) => candleEnd(time, interval)),
    [closed, current, fills, interval],
  );
  const lines = useMemo(() => orderLines(orders ?? nothing), [orders]);

  useEffect(() => {
    reachStart.current = onReachStart;
    hover.current = onHover;
  }, [onReachStart, onHover]);

  useEffect(() => {
    if (!container.current) {
      return undefined;
    }

    const { up, down } = colors();
    const api = createChart(container.current, {
      height: 360,
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: token('--color-canvas', '#101216') },
        textColor: token('--color-muted', '#8c94a3'),
      },
      grid: {
        vertLines: { color: token('--color-line', '#2c313b') },
        horzLines: { color: token('--color-line', '#2c313b') },
      },
      timeScale: { timeVisible: true, secondsVisible: false },
    });
    const candles = api.addSeries(CandlestickSeries, {
      upColor: up,
      downColor: down,
      wickUpColor: up,
      wickDownColor: down,
      borderVisible: false,
    });
    const volume = api.addSeries(HistogramSeries, {
      priceScaleId: 'volume',
      priceFormat: { type: 'volume' },
      lastValueVisible: false,
      priceLineVisible: false,
    });
    volume.priceScale().applyOptions({ scaleMargins: { top: 0.8, bottom: 0 }, visible: false });
    const onRange = (range: LogicalRange | null) => {
      if (!applying.current && range && range.from < loadAheadBars) {
        reachStart.current();
      }
    };
    api.timeScale().subscribeVisibleLogicalRangeChange(onRange);
    const onCrosshair = (moved: MouseEventParams) =>
      hover.current?.(typeof moved.time === 'number' ? moved.time : null);
    api.subscribeCrosshairMove(onCrosshair);
    chart.current = {
      api,
      candles,
      volume,
      markers: createSeriesMarkers(candles, []),
      lines: [],
    };

    return () => {
      api.timeScale().unsubscribeVisibleLogicalRangeChange(onRange);
      api.unsubscribeCrosshairMove(onCrosshair);
      api.remove();
      chart.current = null;
      firstTime.current = null;
      drawnHistory.current = null;
    };
  }, []);

  useEffect(() => {
    const drawn = chart.current;

    if (!drawn) {
      return;
    }

    applying.current = true;
    try {
      const scale = drawn.api.timeScale();
      const visible = scale.getVisibleLogicalRange();
      const previousFirst = firstTime.current;
      drawn.candles.setData(closed.map(candleOf));
      drawn.volume.setData(volumeBars(closed).map(volumeColumn));

      if (closed.length > 0 && previousFirst === null) {
        scale.setVisibleLogicalRange({
          from: Math.max(0, closed.length - initialBars),
          to: closed.length + 2,
        });
      } else if (visible && previousFirst !== null) {
        // Older candles were added on the left: move the view by them so it stays on the same candles.
        const added = closed.findIndex((bar) => bar.time >= previousFirst);

        if (added > 0) {
          scale.setVisibleLogicalRange({ from: visible.from + added, to: visible.to + added });
        }
      }
    } finally {
      applying.current = false;
    }

    firstTime.current = closed[0]?.time ?? null;
  }, [closed]);

  useEffect(() => {
    const drawn = chart.current;

    if (!drawn) {
      return;
    }

    // `setData` above dropped earlier current candles, so a new history draws them all again; otherwise only the newest
    // changed, and `update` refuses a candle older than the last one drawn.
    const redraw = drawnHistory.current !== closed;
    drawnHistory.current = closed;

    for (const bar of redraw ? current : current.slice(-1)) {
      drawn.candles.update({ ...candleOf(bar), ...formingStyle(bar) });
      drawn.volume.update(volumeColumn(volumeBars([bar])[0]!));
    }
  }, [closed, current]);

  useEffect(() => {
    const { up, down } = colors();

    chart.current?.markers.setMarkers(
      markers.map((marker) => ({
        time: marker.time as UTCTimestamp,
        position: marker.position,
        shape: marker.shape,
        price: marker.price,
        text: marker.text,
        color: marker.shape === 'arrowUp' ? up : down,
      })),
    );
  }, [markers]);

  useEffect(() => {
    const drawn = chart.current;

    if (!drawn) {
      return;
    }

    const { up, down } = colors();

    for (const line of drawn.lines) {
      drawn.candles.removePriceLine(line);
    }

    drawn.lines = lines.map((line) =>
      drawn.candles.createPriceLine({
        price: line.price,
        color: line.side === 'buy' ? up : down,
        lineStyle: dashed,
        lineWidth: 1,
        axisLabelVisible: true,
        title: line.title,
      }),
    );
  }, [lines]);

  return (
    <figure
      role="img"
      aria-label={
        fills === undefined && orders === undefined
          ? label
          : `${label}, исполнений: ${markers.length}, ордеров: ${lines.length}`
      }
      className="m-0 h-90 w-full overflow-hidden rounded-md"
    >
      <div ref={container} className="size-full" />
    </figure>
  );
}
