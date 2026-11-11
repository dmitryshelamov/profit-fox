import { followHub } from '@/shared/realtime/followHub';

import type { LiveFeed, LiveMinute, LiveSnapshot } from '../model/tradeFlow';

export interface TradeFlowHandlers {
  /** `superseded`: a message came after `Join` was invoked, so the snapshot is older than it. */
  onSnapshot: (snapshot: LiveSnapshot, superseded: boolean) => void;
  onMinute: (minute: LiveMinute) => void;
  onStatus: (status: LiveFeed) => void;
  /** The link to the server is lost or being restored, or the join failed: nothing is arriving. */
  onLost: () => void;
}

/**
 * Follows the current minute of the trade flow of a pair over `/api/hubs/trade-flow` (see `followHub`): `Join` answers with the state of the feed
 * and its latest sums, then `TradeFlowMinute` and `TradeFlowStatus` come; a pair without a trade flow is not tried again. Returns the function that
 * stops following.
 */
export function followTradeFlow(
  category: string,
  symbol: string,
  handlers: TradeFlowHandlers,
): () => void {
  return followHub<LiveSnapshot>({
    url: '/api/hubs/trade-flow',
    method: 'Join',
    args: [category, symbol],
    refusal: 'trade_flow_not_found',
    messages: { TradeFlowMinute: handlers.onMinute, TradeFlowStatus: handlers.onStatus },
    onSnapshot: handlers.onSnapshot,
    onLost: handlers.onLost,
  });
}
