import { resultColumns } from './resultColumns';

const metrics = {
  return: '18.42',
  maxDrawdown: '6.10',
  returnToDrawdown: '3.02',
  closedTrades: 41,
  winRate: '58.5',
  fees: '21.70',
  pnl: '184.20',
};

const cells = (value: typeof metrics) =>
  Object.fromEntries(resultColumns.map((column) => [column.code, column.show(value as never)]));

describe('result columns', () => {
  it('sorts by the codes of the API metrics', () => {
    expect(resultColumns.map((column) => column.code)).toEqual([
      'return',
      'maxDrawdown',
      'returnToDrawdown',
      'closedTrades',
      'winRate',
      'fees',
      'pnl',
    ]);
  });

  it('shows percents with a sign for the return and the pnl, as the strings come', () => {
    expect(cells(metrics)).toEqual({
      return: '+18.42 %',
      maxDrawdown: '6.10 %',
      returnToDrawdown: '3.02',
      closedTrades: '41',
      winRate: '58.50 %',
      fees: '21.70',
      pnl: '+184.20',
    });
  });

  it('rounds long decimals to two places and keeps the exact value for a tooltip', () => {
    const long = {
      ...metrics,
      return: '-0.379810000000',
      maxDrawdown: '0.66757603223461631653100967',
      returnToDrawdown: '-0.568938939776851727',
      fees: '3.7611190000000000000000000000',
      pnl: '-40.36465900000000000000',
    };

    expect(cells(long)).toMatchObject({
      return: '-0.38 %',
      maxDrawdown: '0.67 %',
      returnToDrawdown: '-0.57',
      fees: '3.76',
      pnl: '-40.36',
    });
    expect(resultColumns.find((column) => column.code === 'fees')!.exact(long as never)).toBe(
      '3.7611190000000000000000000000',
    );
  });

  it('shows a dash where the metric is missing', () => {
    const shown = cells({ ...metrics, returnToDrawdown: null as never, winRate: null as never });

    expect(shown.returnToDrawdown).toBe('—');
    expect(shown.winRate).toBe('—');
  });

  it('colors the return and the pnl by their sign', () => {
    const tone = (code: string, value: string) =>
      resultColumns
        .find((column) => column.code === code)!
        .tone?.({ ...metrics, [code]: value } as never);

    expect(tone('return', '18.42')).toBe('ok');
    expect(tone('return', '-1.04')).toBe('err');
    expect(tone('pnl', '0')).toBeUndefined();
    expect(tone('maxDrawdown', '6.10')).toBeUndefined();
  });

  it('puts the best first: the lowest drawdown and fees, the highest of the rest', () => {
    expect(Object.fromEntries(resultColumns.map((column) => [column.code, column.best]))).toEqual({
      return: 'desc',
      maxDrawdown: 'asc',
      returnToDrawdown: 'desc',
      closedTrades: 'desc',
      winRate: 'desc',
      fees: 'asc',
      pnl: 'desc',
    });
  });

  it('titles the columns', () => {
    expect(resultColumns.map((column) => column.title)).toEqual([
      'Доходность',
      'Макс. просадка',
      'Дох. / просадка',
      'Сделки',
      'Прибыльных',
      'Комиссии',
      'PnL',
    ]);
  });
});
