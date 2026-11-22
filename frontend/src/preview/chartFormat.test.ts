import { monthTick, priceText } from './chartFormat';

describe('the scales of the chart', () => {
  it('names a month in three Russian letters, by UTC', () => {
    expect(monthTick(Date.UTC(2025, 7, 1) / 1000)).toBe('авг');
    expect(monthTick(Date.UTC(2025, 8, 30, 23) / 1000)).toBe('сен');
    expect(monthTick(Date.UTC(2026, 4, 1) / 1000)).toBe('май');
    // The first hour of January in UTC is still January to the west of Greenwich.
    expect(monthTick(Date.UTC(2026, 0, 1) / 1000)).toBe('янв');
  });

  it('writes a price with the digits its size needs', () => {
    expect(priceText(120000)).toBe('120\u00A0000');
    expect(priceText(79069.5)).toBe('79\u00A0070');
    expect(priceText(3456.78)).toBe('3\u00A0457');
    expect(priceText(142.5)).toBe('142,50');
    expect(priceText(1.2345)).toBe('1,2345');
    expect(priceText(0.012345)).toBe('0,012345');
    expect(priceText(0.00001234)).toBe('0,00001234');
    expect(priceText(0)).toBe('0');
  });
});
