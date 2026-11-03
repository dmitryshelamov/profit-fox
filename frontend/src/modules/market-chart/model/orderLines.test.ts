import { type OrderInput, orderLines } from './orderLines';

const order = (overrides: Partial<OrderInput> = {}): OrderInput => ({
  side: 'sell',
  type: 'limit',
  status: 'active',
  price: '105000',
  quantity: '0.01',
  filledQuantity: '0',
  ...overrides,
});

describe('orderLines', () => {
  it('draws an active limit order at its price with side, price and the quantity left', () => {
    expect(orderLines([order()])).toEqual([
      { price: 105000, side: 'sell', title: 'Продажа 105000 · 0.01' },
    ]);
    expect(orderLines([order({ side: 'buy', price: '99500.5' })])[0]?.title).toBe(
      'Покупка 99500.5 · 0.01',
    );
  });

  it('shows the quantity left of a partly filled order without float math', () => {
    expect(orderLines([order({ quantity: '0.3', filledQuantity: '0.1' })])[0]?.title).toBe(
      'Продажа 105000 · 0.2',
    );
    expect(orderLines([order({ quantity: '1', filledQuantity: '0.25' })])[0]?.title).toBe(
      'Продажа 105000 · 0.75',
    );
    expect(orderLines([order({ quantity: '1.5', filledQuantity: '0.5' })])[0]?.title).toBe(
      'Продажа 105000 · 1',
    );
  });

  it('leaves out orders that do not stand in the book as limit orders', () => {
    expect(
      orderLines([
        order({ type: 'market' }),
        order({ status: 'filled' }),
        order({ status: 'cancelled' }),
        order({ price: null }),
        order({ quantity: null }),
      ]),
    ).toEqual([]);
  });
});
