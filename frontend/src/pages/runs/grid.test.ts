import { setCount, valuesOf } from './grid';

// Change add-strategy-backtest, design.md, decision 3: the form counts the sets by the rule of `BacktestGrid` of the
// server, whose tests (`BacktestGridTests`) give these cases.
describe('the count of the sets of the form', () => {
  // Spec bench-run-queue «Форма запуска прогона», scenario «Число наборов».
  it('is six for three thresholds on 1h and 4h', () => {
    expect(setCount([valuesOf('0.0003, 0.0005, 0.001')], ['1h', '4h'])).toBe(6);
  });

  it('multiplies the values of every parameter that has them', () => {
    expect(setCount([valuesOf('0.01, 0.02'), valuesOf('1000,2000')], ['4h'])).toBe(4);
  });

  it('counts a parameter without values once', () => {
    expect(setCount([valuesOf('0.0005'), valuesOf(' , ')], ['4h'])).toBe(1);
    expect(setCount([], ['4h'])).toBe(1);
  });

  it('counts a timeframe given twice once and a value given twice twice, as the server does', () => {
    expect(setCount([valuesOf('0.001, 0.001')], ['4h', '4h'])).toBe(2);
  });

  it('is none without a timeframe', () => {
    expect(setCount([valuesOf('0.001')], [])).toBe(0);
  });
});
