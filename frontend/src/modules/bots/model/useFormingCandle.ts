import { useEffect, useMemo, useReducer } from 'react';

import { followFormingCandle } from '../realtime/followFormingCandle';
import {
  type FormingCandle,
  formingCandles,
  type FormingFeed,
  formingReducer,
  initialForming,
} from './formingCandle';

/**
 * The current candle of the bot and the state of its channel. The channel opens with the page and closes with it; a lost
 * link shows as no data and the connection restores itself, so the chart never depends on it.
 */
export function useFormingCandle(botId: string): { feed: FormingFeed; live: FormingCandle[] } {
  const [state, dispatch] = useReducer(formingReducer, initialForming);

  useEffect(() => {
    dispatch({ type: 'reset' });

    return followFormingCandle(botId, {
      onSnapshot: (snapshot, superseded) => dispatch({ type: 'snapshot', snapshot, superseded }),
      onCandle: (candle) => dispatch({ type: 'candle', candle }),
      onStatus: (status) => dispatch({ type: 'status', status }),
      onLost: () => dispatch({ type: 'lost' }),
    });
  }, [botId]);

  const live = useMemo(() => formingCandles(state), [state]);

  return { feed: state.feed, live };
}
