import { parameterRows } from './parameterRows';

describe('parameter rows', () => {
  it('puts a dash where a result has no such parameter', () => {
    expect(parameterRows([{ fast: '12', slow: '26' }, { step: '0.5' }])).toEqual([
      { name: 'fast', values: ['12', '—'] },
      { name: 'slow', values: ['26', '—'] },
      { name: 'step', values: ['—', '0.5'] },
    ]);
  });

  it('keeps the order of first appearance', () => {
    expect(parameterRows([{ b: '1' }, { a: '2', b: '3' }]).map((row) => row.name)).toEqual([
      'b',
      'a',
    ]);
  });

  it('has no rows without parameters', () => {
    expect(parameterRows([{}, {}])).toEqual([]);
  });
});
