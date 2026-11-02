interface PositionOrders {
  readonly orders: readonly {
    readonly side: string;
    readonly fills: readonly { readonly at: string }[];
  }[];
}

/** Every fill of the positions with the side of its order, as the chart marks them. */
export function positionFills(
  positions: readonly PositionOrders[],
): { at: string; side: 'buy' | 'sell' }[] {
  return positions.flatMap((position) =>
    position.orders.flatMap((order) =>
      order.fills.map((fill) => ({
        at: fill.at,
        side: order.side === 'buy' ? ('buy' as const) : ('sell' as const),
      })),
    ),
  );
}
