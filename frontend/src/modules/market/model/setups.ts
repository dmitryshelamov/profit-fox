import { intervalLabel } from '@/modules/market-data';

import type { MarketRow } from './rows';

export type SetupFilter = 'all' | string;

/** The setups the scanner knows, with their names. */
export const setupChoices = [
  { value: 'impulse', label: 'Импульс' },
  { value: 'early_impulse', label: 'Ранний импульс' },
  { value: 'drop', label: 'Падение' },
  { value: 'early_drop', label: 'Раннее падение' },
  { value: 'high_volatility', label: 'Высокая волатильность' },
  { value: 'breakout', label: 'Пробой' },
  { value: 'false_breakout', label: 'Ложный пробой' },
  { value: 'squeeze', label: 'Сжатие' },
  { value: 'exhaustion', label: 'Истощение' },
  { value: 'rsi_extreme', label: 'Перекупленность / перепроданность' },
  { value: 'new_extreme', label: 'Новый максимум / минимум' },
  { value: 'quiet_volume', label: 'Объём без движения' },
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

export const setupsOn = (row: MarketRow, interval: string): readonly string[] =>
  row.setups[interval] ?? [];
