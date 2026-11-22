import { useQuery } from '@tanstack/react-query';

import { listActiveRuns } from '@/api/runs';

import { pollMs } from './labels';

/** How often the queue is asked again while it is empty. */
export const idlePollMs = 30_000;

/**
 * The runs that wait and go, for the header and for the page «Прогоны» under one query key, so the page asks once
 * (spec bench-ui «Состояние очереди в шапке»): every 3 s while the queue has runs, every 30 s while it is empty.
 */
export function useActiveRuns() {
  return useQuery({
    queryKey: ['runs', 'active'],
    queryFn: listActiveRuns,
    refetchInterval: (query) => (query.state.data?.length ? pollMs : idlePollMs),
  });
}
