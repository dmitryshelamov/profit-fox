import { jsonNumberOf } from '@/shared/api/jsonNumber';

import { parameterValuesOf } from './parameterValues';

const declared = [
  { name: 'fast', type: 'number' as const },
  { name: 'slow', type: 'number' as const },
  { name: 'cooldown', type: 'duration' as const },
];

describe('parameterValuesOf', () => {
  it('leaves out empty fields, so the strategy default applies', () => {
    expect(parameterValuesOf(declared, { fast: '10', slow: '', cooldown: '  ' })).toEqual({
      fast: jsonNumberOf('10'),
    });
  });

  it('sends numbers with the digits typed and durations as text', () => {
    expect(parameterValuesOf(declared, { fast: ' 3.5 ', cooldown: '04:00:00' })).toEqual({
      fast: jsonNumberOf('3.5'),
      cooldown: '04:00:00',
    });
  });

  it.each(['abc', '0x10', '1e5', '0,5'])(
    'sends "%s" as it is, so the server rejects it',
    (text) => {
      expect(parameterValuesOf(declared, { fast: text })).toEqual({ fast: text });
    },
  );

  it('ignores a value of a parameter the strategy does not declare', () => {
    expect(parameterValuesOf(declared, { old: '1' })).toEqual({});
  });
});
