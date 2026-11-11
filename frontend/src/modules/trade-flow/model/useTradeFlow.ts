import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useReducer } from 'react';

import { tradeFlowQuery } from '../api';
import { followTradeFlow } from '../realtime/followTradeFlow';
import { initialLiveFlow, liveFlowReducer, panelBars } from './tradeFlow';

/**
 * The bars of the panel and the state of its channel: the closed bars from the API, read again every minute, and the live minutes from the hub,
 * each counted once. The channel opens with the panel and closes with it; a lost link shows as no data and never touches the closed bars.
 */
export function useTradeFlow(category: string, symbol: string, interval: string) {
  const query = useQuery(tradeFlowQuery(category, symbol, interval));
  const [live, dispatch] = useReducer(liveFlowReducer, initialLiveFlow);

  useEffect(() => {
    dispatch({ type: 'reset' });

    return followTradeFlow(category, symbol, {
      onSnapshot: (snapshot, superseded) => dispatch({ type: 'snapshot', snapshot, superseded }),
      onMinute: (minute) => dispatch({ type: 'minute', minute }),
      onStatus: (status) => dispatch({ type: 'status', status }),
      onLost: () => dispatch({ type: 'lost' }),
    });
  }, [category, symbol]);

  const shown = useMemo(
    () =>
      query.data === undefined
        ? undefined
        : panelBars(query.data.bars, query.data.coveredUntil, live.minutes, interval),
    [query.data, live.minutes, interval],
  );

  return { query, feed: live.feed, shown };
}
