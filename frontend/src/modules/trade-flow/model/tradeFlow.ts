import { intervalSeconds } from '@/modules/market-chart';
import { addDecimals } from '@/shared/format/decimal';

/**
 * A bar of buys and sells by the side of the taker, as the API gives it: the volume in coins and the turnover in USDT as exact decimal strings and
 * the number of trades.
 */
export interface FlowBar {
  readonly openTime: string;
  readonly buyVolume: string;
  readonly sellVolume: string;
  readonly buyTurnover: string;
  readonly sellTurnover: string;
  readonly buyTrades: number | string;
  readonly sellTrades: number | string;
}

/**
 * The sums of the current minute as the hub `/api/hubs/trade-flow` sends them; the hub is not in the OpenAPI document, so they are typed here.
 * `since` is the moment the feed subscribed: a minute that began before it is incomplete.
 */
export interface LiveMinute extends FlowBar {
  readonly since: string;
}

/** What the panel shows about the channel: waiting for the first answer, delivering, silent. */
export type LiveFeed = 'connecting' | 'live' | 'noData';

/** What `Join` returns: the state of the feed and its newest sums. */
export interface LiveSnapshot {
  readonly state: LiveFeed;
  readonly flow: LiveMinute | null;
}

/** The state of the channel and the live minutes: the current one and the one before it, kept until the closed bars bring it. */
export interface LiveFlowState {
  readonly feed: LiveFeed;
  readonly minutes: readonly LiveMinute[];
}

export type LiveFlowEvent =
  | {
      readonly type: 'snapshot';
      readonly snapshot: LiveSnapshot;
      /** A minute or a state message arrived while the answer was on its way, so the answer is older than the state. */
      readonly superseded?: boolean;
    }
  | { readonly type: 'minute'; readonly minute: LiveMinute }
  | { readonly type: 'status'; readonly status: LiveFeed }
  | { readonly type: 'lost' }
  | { readonly type: 'reset' };

export const initialLiveFlow: LiveFlowState = { feed: 'connecting', minutes: [] };

function withMinute(state: LiveFlowState, minute: LiveMinute): LiveFlowState {
  const last = state.minutes.at(-1);

  if (last === undefined) {
    return { ...state, minutes: [minute] };
  }

  const at = Date.parse(minute.openTime);
  const known = Date.parse(last.openTime);

  if (at < known) {
    return state;
  }

  // A minute of the next one ends the current one; the same minute just updates it.
  return {
    ...state,
    minutes: at > known ? [last, minute] : [...state.minutes.slice(0, -1), minute],
  };
}

/** Applies one hub event; a minute older than the one shown is ignored. */
export function liveFlowReducer(state: LiveFlowState, event: LiveFlowEvent): LiveFlowState {
  switch (event.type) {
    case 'snapshot': {
      if (event.superseded) {
        return state;
      }

      const next = { ...state, feed: event.snapshot.state };

      return event.snapshot.flow ? withMinute(next, event.snapshot.flow) : next;
    }
    case 'minute':
      return { ...withMinute(state, event.minute), feed: 'live' };
    case 'status':
      return { ...state, feed: event.status };
    case 'lost':
      return { ...state, feed: 'noData' };
    case 'reset':
      return initialLiveFlow;
  }
}

function candleOpen(time: string, interval: string): number {
  const length = (intervalSeconds[interval] ?? 60) * 1000;

  return Math.floor(Date.parse(time) / length) * length;
}

function added(bar: FlowBar, minute: FlowBar): FlowBar {
  return {
    ...bar,
    buyVolume: addDecimals(bar.buyVolume, minute.buyVolume),
    sellVolume: addDecimals(bar.sellVolume, minute.sellVolume),
    buyTurnover: addDecimals(bar.buyTurnover, minute.buyTurnover),
    sellTurnover: addDecimals(bar.sellTurnover, minute.sellTurnover),
    buyTrades: Number(bar.buyTrades) + Number(minute.buyTrades),
    sellTrades: Number(bar.sellTrades) + Number(minute.sellTrades),
  };
}

/**
 * The bars the panel draws (decision 11 of collect-trade-flow): the closed bars, which hold every stored minute before `coveredUntil`, and the live
 * minutes from it on, each added to the bar of its candle once. The current bar is the bar of the newest live minute; it is incomplete while that
 * minute began before the channel joined.
 */
export function panelBars(
  closed: readonly FlowBar[],
  coveredUntil: string,
  minutes: readonly LiveMinute[],
  interval: string,
): { bars: FlowBar[]; current: string | null; incomplete: boolean } {
  const bars = [...closed];
  const fresh = minutes.filter((minute) => Date.parse(minute.openTime) >= Date.parse(coveredUntil));

  for (const minute of fresh) {
    const open = candleOpen(minute.openTime, interval);
    const last = bars.at(-1);

    if (last !== undefined && Date.parse(last.openTime) === open) {
      bars[bars.length - 1] = added(last, minute);
    } else {
      const { since: _since, ...sums } = minute;
      bars.push({ ...sums, openTime: new Date(open).toISOString() });
    }
  }

  const newest = fresh.at(-1);

  return {
    bars,
    current: newest === undefined ? null : bars.at(-1)!.openTime,
    incomplete: newest !== undefined && Date.parse(newest.openTime) < Date.parse(newest.since),
  };
}
