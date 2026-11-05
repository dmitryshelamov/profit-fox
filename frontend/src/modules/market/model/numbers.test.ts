import {
  changeText,
  compactNumber,
  fundingText,
  instrumentsText,
  shownOfText,
  spreadText,
  trend,
} from './numbers';

describe('compact number', () => {
  // As the table of the mockup: billions with two decimals, otherwise one decimal below a hundred and none above.
  it.each([
    ['48200000', '48.2 млн'],
    ['74000000', '74.0 млн'],
    ['9800000', '9.8 млн'],
    ['386000000', '386 млн'],
    ['1820000000', '1.82 млрд'],
    ['2410000000', '2.41 млрд'],
    ['25300', '25.3 тыс.'],
    ['1000', '1.0 тыс.'],
    ['999.6', '1000'],
    ['532.18', '532'],
    ['0', '0'],
  ])('writes %s as «%s»', (value, text) => {
    expect(compactNumber(value)).toBe(text);
  });
});

describe('funding text', () => {
  it.each([
    ['0.000412', '+0.0412'],
    ['-0.000034', '-0.0034'],
    ['0', '0.0000'],
    ['0.000123456', '+0.0123'],
    ['0.01', '+1.0000'],
  ])('writes the rate %s in percent as «%s»', (rate, text) => {
    expect(fundingText(rate)).toBe(text);
  });
});

describe('change text', () => {
  it.each([
    ['1.92', 2, '+1.92'],
    ['-0.06', 2, '-0.06'],
    ['0', 2, '0.00'],
    ['7.84', 1, '+7.8'],
    ['-0.8', 1, '-0.8'],
  ])('writes the change %s with %i decimals as «%s»', (value, digits, text) => {
    expect(changeText(value, digits)).toBe(text);
  });
});

describe('spread text', () => {
  it.each([
    ['0.0812', '0.08'],
    ['0.18', '0.18'],
    ['0.01', '0.01'],
    ['0.0049', '<0.01'],
    ['0', '0.00'],
  ])('writes the spread %s as «%s»', (value, text) => {
    expect(spreadText(value)).toBe(text);
  });
});

describe('instruments text', () => {
  it.each([
    [1, '1 инструмент'],
    [3, '3 инструмента'],
    [5, '5 инструментов'],
    [12, '12 инструментов'],
    [21, '21 инструмент'],
    [1104, '1\u00a0104 инструмента'],
  ])('counts %i as «%s»', (count, text) => {
    expect(instrumentsText(count)).toBe(text);
  });
});

describe('shown of instruments', () => {
  // Scenario "Импульсы на 15 минутах".
  it.each([
    [7, 1104, '7 из 1\u00a0104 инструментов'],
    [4, 4, '4 из 4 инструментов'],
    [0, 21, '0 из 21 инструментов'],
  ])('counts %i of %i as «%s»', (shown, total, text) => {
    expect(shownOfText(shown, total)).toBe(text);
  });
});

describe('trend', () => {
  it('tells growth, fall and no change apart', () => {
    expect(trend('0.01')).toBe('up');
    expect(trend('-0.01')).toBe('down');
    expect(trend('0')).toBe('flat');
    expect(trend('0.00')).toBe('flat');
  });
});
