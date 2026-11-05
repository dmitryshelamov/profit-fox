import { marketRow } from '@/test/fixtures';

import { filterBySetup, setupIntervals, setupLabel, setupOptions } from './setups';

const none: Record<string, string[]> = { '5': [], '15': [], '60': [], '240': [] };
const pair = (symbol: string, setups: Record<string, string[]>) =>
  marketRow({ symbol, setups: { ...none, ...setups } });

const rows = [
  pair('A', { '15': ['impulse', 'high_volatility'] }),
  pair('B', { '15': ['drop'], '5': ['impulse'] }),
  pair('C', { '15': ['impulse'] }),
  pair('D', {}),
];

describe('setup chips', () => {
  it('offers all and each setup with the number of pairs that have it on the interval', () => {
    expect(setupOptions(rows, '15')).toEqual([
      { value: 'all', label: 'Все', count: 4 },
      { value: 'impulse', label: 'Импульс', count: 2 },
      { value: 'early_impulse', label: 'Ранний импульс', count: 0 },
      { value: 'drop', label: 'Падение', count: 1 },
      { value: 'early_drop', label: 'Раннее падение', count: 0 },
      { value: 'high_volatility', label: 'Высокая волатильность', count: 1 },
    ]);
  });

  it('counts on the chosen interval only', () => {
    expect(setupOptions(rows, '5').find((option) => option.value === 'impulse')?.count).toBe(1);
  });
});

describe('filterBySetup', () => {
  // Scenario "Импульсы на 15 минутах".
  it('keeps the pairs with the setup on the interval', () => {
    expect(filterBySetup(rows, 'impulse', '15').map((row) => row.symbol)).toEqual(['A', 'C']);
  });

  it('keeps every pair for all', () => {
    expect(filterBySetup(rows, 'all', '15')).toHaveLength(4);
  });
});

describe('labels', () => {
  it('names the setups and the intervals', () => {
    expect(setupLabel('early_drop')).toBe('Раннее падение');
    expect(setupIntervals.map((interval) => interval.label)).toEqual([
      '5 мин',
      '15 мин',
      '1 ч',
      '4 ч',
    ]);
  });

  it('shows an unknown setup as its code', () => {
    expect(setupLabel('mystery')).toBe('mystery');
  });
});
