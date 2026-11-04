import { marketRow as row } from '@/test/fixtures';

import { type CategoryFilter, categoryOptions, filterRows, type MarketRow } from './rows';

const rows = [
  row({ symbol: 'BTCUSDT' }),
  row({ symbol: 'ETHUSDT' }),
  row({ category: 'spot', symbol: 'BTCUSDT' }),
  row({ category: 'spot', symbol: 'SOLUSDT' }),
  row({ category: 'spot', symbol: 'WIFUSDT' }),
];

describe('category options', () => {
  it('lists all, linear and spot with the number of pairs of each', () => {
    expect(categoryOptions(rows)).toEqual([
      { value: 'all', label: 'Все', count: 5 },
      { value: 'linear', label: 'linear', count: 2 },
      { value: 'spot', label: 'spot', count: 3 },
    ]);
  });
});

describe('filter rows', () => {
  const keys = (filtered: MarketRow[]) => filtered.map((item) => `${item.category} ${item.symbol}`);

  it.each<[CategoryFilter, string[]]>([
    ['all', ['linear BTCUSDT', 'linear ETHUSDT', 'spot BTCUSDT', 'spot SOLUSDT', 'spot WIFUSDT']],
    ['linear', ['linear BTCUSDT', 'linear ETHUSDT']],
    ['spot', ['spot BTCUSDT', 'spot SOLUSDT', 'spot WIFUSDT']],
  ])('keeps the pairs of the %s category', (category, expected) => {
    expect(keys(filterRows(rows, category, ''))).toEqual(expected);
  });

  it('searches by part of the symbol without regard to case', () => {
    expect(keys(filterRows(rows, 'all', 'sol'))).toEqual(['spot SOLUSDT']);
    expect(keys(filterRows(rows, 'linear', 'USD'))).toEqual(['linear BTCUSDT', 'linear ETHUSDT']);
    expect(keys(filterRows(rows, 'linear', 'wif'))).toEqual([]);
  });
});
