import { intervalLabel } from '@/modules/market-data';

import type { MarketRow } from './rows';

export type SetupFilter = 'all' | string;

export interface SetupOption {
  readonly value: SetupFilter;
  readonly label: string;
  readonly count: number;
}

/** The setups the scanner knows, with their names. */
export const setupChoices = [
  { value: 'impulse', label: 'Импульс' },
  { value: 'early_impulse', label: 'Ранний импульс' },
  { value: 'drop', label: 'Падение' },
  { value: 'early_drop', label: 'Раннее падение' },
  { value: 'high_volatility', label: 'Высокая волатильность' },
] as const;

/** The intervals the setups are read on. */
export const setupIntervals = ['5', '15', '60', '240'].map((value) => ({
  value,
  label: intervalLabel(value),
}));

export const defaultSetupInterval = '15';

/** The name of a setup by its code; a code the screen does not know is shown as it is. */
export function setupLabel(code: string): string {
  return setupChoices.find((setup) => setup.value === code)?.label ?? code;
}

const setupsOn = (row: MarketRow, interval: string): readonly string[] =>
  row.setups[interval] ?? [];

/** The chips of the setups: all pairs, then each setup, each with the number of pairs that have it on the interval. */
export function setupOptions(rows: readonly MarketRow[], interval: string): SetupOption[] {
  return [
    { value: 'all', label: 'Все', count: rows.length },
    ...setupChoices.map((setup) => ({
      ...setup,
      count: rows.filter((row) => setupsOn(row, interval).includes(setup.value)).length,
    })),
  ];
}

/** The pairs that have the setup on the interval; every pair for «Все». */
export function filterBySetup(
  rows: readonly MarketRow[],
  setup: SetupFilter,
  interval: string,
): MarketRow[] {
  return setup === 'all'
    ? [...rows]
    : rows.filter((row) => setupsOn(row, interval).includes(setup));
}

export { setupsOn };
