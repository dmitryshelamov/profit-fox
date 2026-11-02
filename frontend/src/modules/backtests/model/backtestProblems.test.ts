import { backtestFieldErrors } from './backtestProblems';

describe('backtest validation problems', () => {
  it.each([
    'from',
    'to',
    'period',
    'parameters',
    'searchMode',
    'simulation.balance',
    'simulation.limitFeePercent',
    'simulation.marketFeePercent',
    'simulation.slippagePercent',
    'simulation.limitFillMode',
  ])('puts %s on its field', (key) => {
    expect(backtestFieldErrors({ [key]: ['bad'] })).toEqual({
      fields: { [key]: ['bad'] },
      form: [],
    });
  });

  it('puts a parameter on the field of that parameter', () => {
    expect(backtestFieldErrors({ 'parameters.fast': ['Range is invalid.'] })).toEqual({
      fields: { 'parameters.fast': ['Range is invalid.'] },
      form: [],
    });
  });

  it('puts a key that matches no field into the form', () => {
    expect(backtestFieldErrors({ simulation: ['Not an object.'] })).toEqual({
      fields: {},
      form: ['Not an object.'],
    });
  });

  it('has no errors when the answer has none', () => {
    expect(backtestFieldErrors(undefined)).toEqual({ fields: {}, form: [] });
  });
});
