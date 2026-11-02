import { positionFills } from './positionFills';

const order = (side: string, fills: { at: string }[]) => ({
  side,
  fills: fills.map((fill) => ({ ...fill, price: '1', quantity: '1', fee: '0', feeAsset: 'quote' })),
});

describe('positionFills', () => {
  it('takes every fill of every order of the positions with the side of its order', () => {
    expect(
      positionFills([
        { orders: [order('buy', [{ at: '2024-01-05T14:00:00Z' }]), order('sell', [])] },
        { orders: [order('sell', [{ at: '2024-01-11T09:00:00Z' }])] },
      ]),
    ).toEqual([
      { at: '2024-01-05T14:00:00Z', side: 'buy' },
      { at: '2024-01-11T09:00:00Z', side: 'sell' },
    ]);
  });
});
