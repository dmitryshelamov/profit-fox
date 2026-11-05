import { priceByTick } from './price';

describe('priceByTick', () => {
  // Scenario "Вход по рынку и лимитный вход".
  it('gives every price of a pair the decimals of its price step', () => {
    expect(priceByTick('184.7', '0.01')).toBe('184.70');
    expect(priceByTick('197.00', '0.01')).toBe('197.00');
  });

  it.each([
    ['0.5', '12.5', '12.5'],
    ['0.5', '12', '12.0'],
    ['1', '2800', '2 800'],
    ['0.0001', '0.2', '0.2000'],
  ])('shows the step %s as the price %s as %s', (tick, price, expected) => {
    expect(priceByTick(price, tick)).toBe(expected);
  });

  it('shows the price as it is while the pair has no price step', () => {
    expect(priceByTick('184.7', null)).toBe('184.7');
  });
});
