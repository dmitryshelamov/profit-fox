import { useEffect, useState } from 'react';

import { type LiveCandle, liveCandleUrl } from '@/api/market';

/** How long the page waits before it connects again to a stream that broke. */
export const reconnectMs = 2000;

/** The live candle of a pair: the last one, the closed ones of the session, and whether the stream goes. */
export interface LiveState {
  readonly candle: LiveCandle | null;
  readonly closed: readonly LiveCandle[];
  readonly state: 'connecting' | 'live' | 'stopped';
}

/**
 * The live candle of a pair on a timeframe from the stream of the API (spec market-overview «Живая свеча»): each event
 * `candle` replaces the last candle; a closed one is kept. A broken stream says so and connects again after
 * {@link reconnectMs}. The stream is closed with the page: the API then stops taking the forming candles.
 */
export function useLiveCandle(symbol: string, timeframe: string): LiveState {
  const [live, setLive] = useState<LiveState>({ candle: null, closed: [], state: 'connecting' });

  useEffect(() => {
    setLive({ candle: null, closed: [], state: 'connecting' });
    if (typeof EventSource === 'undefined') {
      return;
    }

    let source: EventSource | null = null;
    let timer: ReturnType<typeof setTimeout> | null = null;
    let gone = false;
    const connect = () => {
      source = new EventSource(liveCandleUrl(symbol, timeframe));
      source.addEventListener('candle', (event) => {
        const candle = JSON.parse((event as MessageEvent<string>).data) as LiveCandle;
        setLive((old) => ({
          candle,
          closed: candle.closed
            ? [...old.closed.filter((item) => item.openTime !== candle.openTime), candle]
            : old.closed,
          state: 'live',
        }));
      });
      source.addEventListener('error', () => {
        source?.close();
        if (gone) {
          return;
        }

        setLive((old) => ({ ...old, state: 'stopped' }));
        timer = setTimeout(connect, reconnectMs);
      });
    };
    connect();

    return () => {
      gone = true;
      if (timer !== null) {
        clearTimeout(timer);
      }

      source?.close();
    };
  }, [symbol, timeframe]);

  return live;
}
