import { compareDecimals, multiplyDecimals, shiftDecimal } from './decimal';

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
