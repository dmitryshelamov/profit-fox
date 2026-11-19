import type { PreviewCandle, PreviewEvent, RequestError } from '@/api/client';

/** The timeframes of a signal, as the hypothesis files write them. */
export const timeframes = ['1h', '4h', '1d'] as const;

const timeframeMs: Record<string, number> = {
  '1h': 3_600_000,
  '4h': 14_400_000,
  '1d': 86_400_000,
};

/** The length of a timeframe in milliseconds. */
export function lengthOf(timeframe: string): number {
  const length = timeframeMs[timeframe];
  if (length === undefined) {
    throw new Error(`not a timeframe of a signal: ${timeframe}`);
  }

  return length;
}

/** A moment of the bench as UTC, `2026-05-15 20:00`: the reports and the specs write time this way. */
export function formatUtc(ms: number | string): string {
  return new Date(Number(ms)).toISOString().slice(0, 16).replace('T', ' ');
}

/** A day of the bench as UTC, `2026-05-15`. */
export function formatUtcDay(ms: number | string): string {
  return new Date(Number(ms)).toISOString().slice(0, 10);
}

/** Where the chart puts the mark of an event: on the candle that closes at its moment. */
export interface EventMark {
  readonly time: number;
  readonly moment: number;
}

/** The marks of the events, on the open time of the candle each event closed, in seconds as the chart counts time. */
export function marksOf(events: readonly PreviewEvent[], timeframe: string): EventMark[] {
  const length = lengthOf(timeframe);
  return events.map((event) => {
    const moment = Number(event.moment);
    return { time: (moment - length) / 1000, moment };
  });
}

/**
 * Since when the pair has history: its first candle with trades. The covered windows of the service start earlier
 * than the candles (on soyo-02 in 2023-11 against candles from 2025-11), so they are not used.
 */
export function historyStart(candles: readonly PreviewCandle[]): number | null {
  const first = candles[0];
  return first === undefined ? null : Number(first.openTime);
}

/** The close price of the candle that closed at the moment of an event. */
export function closeAt(
  candles: readonly PreviewCandle[],
  moment: number,
  timeframe: string,
): number | null {
  const open = moment - lengthOf(timeframe);
  const candle = candles.find((item) => Number(item.openTime) === open);
  return candle === undefined ? null : Number(candle.close);
}

/** Where in the request a problem is: a wire, a pin, a setting, a node, a parameter or a field. */
export function placeOf(error: RequestError): string {
  if (error.wire) {
    return `провод ${error.wire.from} → ${error.wire.to}`;
  }

  if (error.node && error.pin) {
    return `${error.node}.${error.pin}`;
  }

  if (error.node && error.setting) {
    return `узел ${error.node}, настройка ${error.setting}`;
  }

  if (error.nodes && error.nodes.length > 0) {
    return `цикл ${error.nodes.join(' → ')}`;
  }

  if (error.node) {
    return `узел ${error.node}`;
  }

  if (error.parameter) {
    return `параметр ${error.parameter}`;
  }

  return error.field ?? 'граф';
}
