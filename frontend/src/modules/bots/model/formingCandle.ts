import type { LiveCandle } from '@/modules/market-chart';

/**
 * The messages of the `/api/hubs/bot-chart` hub. The hub is not in the OpenAPI document, so they are typed here. Prices
 * and volume are exact decimal strings; the candle lives in memory only and the closed history stays the truth.
 */
export type FormingCandle = LiveCandle;

/** What the page shows about the channel: waiting for the first answer, delivering, silent. */
export type FormingFeed = 'connecting' | 'live' | 'noData';

/** What `Subscribe` returns: the state of the feed and its latest candle. */
export interface FormingSnapshot {
  readonly state: FormingFeed;
  readonly candle: FormingCandle | null;
}

export interface FormingState {
  readonly feed: FormingFeed;
  /** The candle being formed, or the newest one received. */
  readonly current: FormingCandle | null;
  /** The candle before it, kept so the chart has no gap until the history brings the closed candle. */
  readonly completed: FormingCandle | null;
}

export type FormingEvent =
  | {
      readonly type: 'snapshot';
      readonly snapshot: FormingSnapshot;
      /** A candle or a state message arrived while the answer was on its way, so the answer is older than the state. */
      readonly superseded?: boolean;
    }
  | { readonly type: 'candle'; readonly candle: FormingCandle }
  | { readonly type: 'status'; readonly status: FormingFeed }
  | { readonly type: 'lost' }
  | { readonly type: 'reset' };

export const initialForming: FormingState = { feed: 'connecting', current: null, completed: null };

function withCandle(state: FormingState, candle: FormingCandle): FormingState {
  if (state.current === null) {
    return { ...state, current: candle };
  }

  const at = Date.parse(candle.openTime);
  const known = Date.parse(state.current.openTime);

  if (at < known) {
    return state;
  }

  // A candle of the next interval ends the current one; the same interval just updates it.
  return at > known
    ? { ...state, completed: state.current, current: candle }
    : { ...state, current: candle };
}

/** Applies one hub event; a candle older than the one shown is ignored, and so is whatever a late snapshot says about it. */
export function formingReducer(state: FormingState, event: FormingEvent): FormingState {
  switch (event.type) {
    case 'snapshot': {
      if (event.superseded) {
        const { candle } = event.snapshot;
        const newer =
          candle !== null &&
          (state.current === null ||
            Date.parse(candle.openTime) > Date.parse(state.current.openTime));

        return newer ? withCandle(state, candle) : state;
      }

      const next = { ...state, feed: event.snapshot.state };

      return event.snapshot.candle ? withCandle(next, event.snapshot.candle) : next;
    }
    case 'candle':
      return { ...withCandle(state, event.candle), feed: 'live' };
    case 'status':
      return { ...state, feed: event.status };
    case 'lost':
      return { ...state, feed: 'noData' };
    case 'reset':
      return initialForming;
  }
}

/** The candles to draw after the history, oldest first. */
export function formingCandles(state: FormingState): FormingCandle[] {
  return [state.completed, state.current].filter((candle) => candle !== null);
}

export function feedText(feed: FormingFeed): string {
  switch (feed) {
    case 'connecting':
      return 'подключение';
    case 'live':
      return 'в реальном времени';
    case 'noData':
      return 'нет данных';
  }
}
