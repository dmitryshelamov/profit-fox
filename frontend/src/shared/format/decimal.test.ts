import {
  compareDecimals,
  divideDecimals,
  multiplyDecimals,
  shiftDecimal,
  subtractDecimals,
} from './decimal';

describe('compareDecimals', () => {
  it.each([
    ['2', '10', -1],
    ['10', '2', 1],
    ['1.50', '1.5', 0],
    ['-1', '1', -1],
    ['-2.5', '-2.25', -1],
    ['0.1234567890123456789', '0.1234567890123456788', 1],
    ['-0', '0', 0],
  ])('compares %s with %s as %i', (left, right, expected) => {
    expect(compareDecimals(left, right)).toBe(expected);
  });
});

describe('multiplyDecimals', () => {
  it.each([
    ['2', '10', '20'],
    ['1.5', '3.3', '4.95'],
    ['0.1', '0.2', '0.02'],
    ['-2', '10', '-20'],
    ['-2', '-10', '20'],
    ['2.50', '4', '10'],
    ['0', '5.5', '0'],
  ])('multiplies %s by %s into %s without float error', (left, right, expected) => {
    expect(multiplyDecimals(left, right)).toBe(expected);
  });
});

describe('shiftDecimal', () => {
  it.each([
    ['0.0003', 2, '0.03'],
    ['0.46', 2, '46'],
    ['1', 2, '100'],
    ['-0.0412', 2, '-4.12'],
    ['3', -2, '0.03'],
    ['0.03', -2, '0.0003'],
    ['50', -2, '0.5'],
    ['-3.5', -2, '-0.035'],
  ])('moves the point of %s by %i places into %s', (value, places, expected) => {
    expect(shiftDecimal(value, places)).toBe(expected);
  });
});

describe('subtractDecimals', () => {
  it('subtracts exactly across scales and signs', () => {
    expect(subtractDecimals('0.51', '0.64')).toBe('-0.13');
    expect(subtractDecimals('0.3', '0.1')).toBe('0.2');
    expect(subtractDecimals('0.57', '-0.4500')).toBe('1.02');
    expect(subtractDecimals('5', '5.000')).toBe('0');
  });
});

describe('divideDecimals', () => {
  it('divides to the digits asked for, half away from zero', () => {
    expect(divideDecimals('1.2', '21.9', 3)).toBe('0.055');
    expect(divideDecimals('2', '3', 2)).toBe('0.67');
    expect(divideDecimals('-1', '8', 2)).toBe('-0.13');
    expect(divideDecimals('0.5', '0.25', 1)).toBe('2');
  });
});
