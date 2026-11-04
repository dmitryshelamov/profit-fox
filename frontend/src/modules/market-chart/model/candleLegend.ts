import { formatMoment, groupDigits } from '@/shared/format/format';

interface LegendInput {
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly volume: string;
}

export interface CandleLegend {
  readonly time: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly volume: string;
}

/**
 * The line above a candle chart: the candle under the cursor, or the last closed one when the cursor is off the candles.
 * `hovered` is the open time of the cursor in seconds, as the chart tells it.
 */
export function candleLegend(
  candles: readonly LegendInput[],
  hovered: number | null,
): CandleLegend | null {
  const shown =
    (hovered === null
      ? undefined
      : candles.find((candle) => Date.parse(candle.openTime) / 1000 === hovered)) ?? candles.at(-1);

  return shown
    ? {
        time: formatMoment(shown.openTime),
        open: shown.open,
        high: shown.high,
        low: shown.low,
        close: shown.close,
        volume: groupDigits(shown.volume),
      }
    : null;
}
