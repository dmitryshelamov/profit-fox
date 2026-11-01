import { formatValue, parseValue } from './values';

describe('values of inputs', () => {
  it('reads a number, with a comma or a point', () => {
    expect(parseValue('0,01', 'number')).toBe(0.01);
    expect(parseValue(' 200 ', 'number')).toBe(200);
  });

  it('reads a duration such as 01:00:00 or 1.12:00:00', () => {
    expect(parseValue('01:00:00', 'duration')).toBe('01:00:00');
    expect(parseValue('1.12:00:00', 'duration')).toBe('1.12:00:00');
  });

  it('reads true and false', () => {
    expect(parseValue('true', 'boolean')).toBe(true);
    expect(parseValue('false', 'boolean')).toBe(false);
  });

  it('gives no value for empty or unreadable text', () => {
    expect(parseValue('', 'number')).toBeUndefined();
    expect(parseValue('abc', 'number')).toBeUndefined();
    expect(parseValue('1h', 'duration')).toBeUndefined();
    expect(parseValue('yes', 'boolean')).toBeUndefined();
  });

  it('writes a value back as text', () => {
    expect(formatValue(0.01)).toBe('0.01');
    expect(formatValue(undefined)).toBe('');
  });
});
