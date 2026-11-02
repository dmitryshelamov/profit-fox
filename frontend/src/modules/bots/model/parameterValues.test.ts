import { parameterValuesOf } from './parameterValues';

const declared = [
  { name: 'fast', type: 'number' as const },
  { name: 'slow', type: 'number' as const },
  { name: 'cooldown', type: 'duration' as const },
];

describe('parameterValuesOf', () => {
  it('leaves out empty fields, so the strategy default applies', () => {
    expect(parameterValuesOf(declared, { fast: '10', slow: '', cooldown: '  ' })).toEqual({
      fast: 10,
    });
  });

  it('sends numbers as numbers and durations as text', () => {
    expect(parameterValuesOf(declared, { fast: ' 3.5 ', cooldown: '04:00:00' })).toEqual({
      fast: 3.5,
      cooldown: '04:00:00',
    });
  });

  it('sends text that is not a number as it is, so the server rejects it', () => {
    expect(parameterValuesOf(declared, { fast: 'abc' })).toEqual({ fast: 'abc' });
  });

  it('ignores a value of a parameter the strategy does not declare', () => {
    expect(parameterValuesOf(declared, { old: '1' })).toEqual({});
  });
});
