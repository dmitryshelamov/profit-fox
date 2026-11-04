import { marketRow as row } from '@/test/fixtures';

import { sortRows } from './sorting';

const symbols = (rows: { symbol: string }[]) => rows.map((item) => item.symbol);

describe('sort rows', () => {
  const rows = [
    row({ symbol: 'BBB', change1h: '-1.5' }),
    row({ symbol: 'CCC', change1h: null }),
    row({ symbol: 'AAA', change1h: '3' }),
    row({ symbol: 'DDD', change1h: '10.25' }),
  ];

  it('puts the largest growth first when descending', () => {
    expect(symbols(sortRows(rows, 'change1h', 'desc'))).toEqual(['DDD', 'AAA', 'BBB', 'CCC']);
  });

  it('puts the largest fall first when ascending, and pairs without the metric still last', () => {
    expect(symbols(sortRows(rows, 'change1h', 'asc'))).toEqual(['BBB', 'AAA', 'DDD', 'CCC']);
  });

  it('orders numbers by value, not by their text', () => {
    const turnovers = [
      row({ symbol: 'A', turnover24h: '900' }),
      row({ symbol: 'B', turnover24h: '1000000' }),
      row({ symbol: 'C', turnover24h: '25000' }),
    ];

    expect(symbols(sortRows(turnovers, 'turnover24h', 'desc'))).toEqual(['B', 'C', 'A']);
  });

  it('orders by symbol alphabetically', () => {
    expect(symbols(sortRows(rows, 'symbol', 'asc'))).toEqual(['AAA', 'BBB', 'CCC', 'DDD']);
    expect(symbols(sortRows(rows, 'symbol', 'desc'))).toEqual(['DDD', 'CCC', 'BBB', 'AAA']);
  });

  it('does not change the rows it was given', () => {
    const before = symbols(rows);

    sortRows(rows, 'change1h', 'desc');

    expect(symbols(rows)).toEqual(before);
  });
});
