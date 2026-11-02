import {
  combinationsWord,
  formatDay,
  formatMoment,
  formatUtc,
  groupDigits,
  roundDecimal,
  showDecimal,
  signed,
} from './format';

describe('groupDigits', () => {
  it.each([
    ['100', '100'],
    ['1000', '1 000'],
    ['1000.5', '1 000.5'],
    ['1234567.8912', '1 234 567.8912'],
    ['-1234', '-1 234'],
  ])('shows %s as %s without touching the digits', (value, expected) => {
    expect(groupDigits(value)).toBe(expected);
  });

  it('keeps digits of a number beyond double precision', () => {
    expect(groupDigits('12345678901234567890.123456789')).toBe(
      '12 345 678 901 234 567 890.123456789',
    );
  });
});

describe('dates', () => {
  it('shows a moment in UTC as day.month hours:minutes', () => {
    expect(formatMoment('2026-11-02T14:05:00Z')).toBe('02.11 14:05');
  });

  it('shows a day in UTC as day.month.year', () => {
    expect(formatDay('2024-01-01T00:00:00Z')).toBe('01.01.2024');
  });
});

describe('numbers and moments for tables', () => {
  it('shows a moment in UTC as year-month-day hours:minutes', () => {
    expect(formatUtc('2024-02-01T00:00:00Z')).toBe('2024-02-01 00:00');
  });

  it.each([
    ['18.42', '+18.42'],
    ['-1.04', '-1.04'],
    ['0', '0'],
    ['0.00', '0.00'],
  ])('signs %s as %s', (value, text) => {
    expect(signed(value)).toBe(text);
  });
});

describe('combinationsWord', () => {
  it.each([
    [1, 'комбинация'],
    [2, 'комбинации'],
    [5, 'комбинаций'],
    [11, 'комбинаций'],
    [21, 'комбинация'],
    [240, 'комбинаций'],
  ])('%i -> %s', (count, word) => {
    expect(combinationsWord(count)).toBe(word);
  });
});

describe('roundDecimal', () => {
  it.each([
    ['0.66757603223461631653100967', 2, '0.67'],
    ['3.7611190000000000000000000000', 2, '3.76'],
    ['-0.568938939776851727', 2, '-0.57'],
    ['1741712.94660', 0, '1741713'],
    ['0.125', 2, '0.13'],
    ['-0.125', 2, '-0.13'],
    ['0.994', 2, '0.99'],
    ['0.995', 2, '1.00'],
    ['99.999', 2, '100.00'],
    ['5', 2, '5.00'],
    ['2.5', 0, '3'],
    ['7.9166666666666', 1, '7.9'],
    ['-0.001', 2, '0.00'],
    ['12345678901234567890.999', 2, '12345678901234567891.00'],
  ])('rounds %s to %i decimals as %s', (value, digits, expected) => {
    expect(roundDecimal(value, digits)).toBe(expected);
  });
});

describe('showDecimal', () => {
  it('rounds, groups the thousands and puts a sign in front when asked', () => {
    expect(showDecimal('1234567.8951', 2)).toBe('1 234 567.90');
    expect(showDecimal('18.4249', 2, true)).toBe('+18.42');
    expect(showDecimal('-0.001', 2, true)).toBe('0.00');
    expect(showDecimal('1741712.9466', 0)).toBe('1 741 713');
  });
});
