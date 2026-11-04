import { intervalLabel } from '@/modules/market-data';

export interface PairInterval {
  readonly value: string;
  readonly label: string;
}

/** The intervals the chart of a pair offers. */
export const pairIntervals: readonly PairInterval[] = ['1', '5', '15', '60', '240', 'D'].map(
  (value) => ({ value, label: intervalLabel(value) }),
);

const defaultInterval = '15';

/** The interval the chart shows: the one in the address if the switcher offers it, otherwise 15 min. */
export function shownPairInterval(asked: string | undefined): string {
  return pairIntervals.find((item) => item.value === asked)?.value ?? defaultInterval;
}
