import { useQuery } from '@tanstack/react-query';

import { getRiskState, riskRefreshMs, riskStateKey } from '@/api/risk';

/** The state of the risk module, read again every 5 s; observers of one page share one request. */
export function useRiskState() {
  return useQuery({
    queryKey: riskStateKey,
    queryFn: getRiskState,
    refetchInterval: riskRefreshMs,
  });
}
