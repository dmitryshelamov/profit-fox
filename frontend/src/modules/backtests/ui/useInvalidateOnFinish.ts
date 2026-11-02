import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';

import { isFinished } from '../model/status';

/**
 * When a backtest turns from unfinished to finished, the list of bots (it shows the last backtest) and the backtests
 * of the bot are stale. `statuses` are the statuses of the backtests the page watches, by id; `botId` is the bot of them.
 */
export function useInvalidateOnFinish(
  statuses: Readonly<Record<string, string>> | undefined,
  botId: string | undefined,
) {
  const queryClient = useQueryClient();
  const seen = useRef<Readonly<Record<string, string>>>({});

  useEffect(() => {
    if (!statuses || botId === undefined) {
      return;
    }

    const finishedNow = Object.entries(statuses).some(
      ([id, status]) =>
        isFinished(status) && seen.current[id] !== undefined && !isFinished(seen.current[id]),
    );
    seen.current = statuses;

    if (finishedNow) {
      void queryClient.invalidateQueries({ queryKey: ['bots'], exact: true });
      void queryClient.invalidateQueries({ queryKey: ['bots', botId, 'backtests'] });
    }
  }, [statuses, botId, queryClient]);
}
