/** The fields of an order of a live position that decide whether and how it is drawn. */
export interface OrderInput {
  readonly side: string;
  readonly type: string;
  readonly status: string;
  readonly price: string | null;
  readonly quantity: string | null;
  readonly filledQuantity: string;
}

export interface OrderLine {
  readonly price: number;
  readonly side: string;
  readonly title: string;
}

const scaleOf = (value: string) => value.split('.')[1]?.length ?? 0;

/** `quantity − filled` on decimal strings, so no digit is lost to float math. */
function remainder(quantity: string, filled: string): string {
  const scale = Math.max(scaleOf(quantity), scaleOf(filled));
  const units = (value: string) =>
    BigInt(value.replace('.', '') + '0'.repeat(scale - scaleOf(value)));
  const left = (units(quantity) - units(filled)).toString().padStart(scale + 1, '0');
  const whole = left.slice(0, left.length - scale);
  const fraction = left.slice(left.length - scale).replace(/0+$/, '');

  return fraction === '' ? whole : `${whole}.${fraction}`;
}

/**
 * A dashed line for each limit order standing in the book, titled with its side, price and what is still left of it
 * (`quantity − filledQuantity`).
 */
export function orderLines(orders: readonly OrderInput[]): OrderLine[] {
  return orders.flatMap((order) =>
    order.type === 'limit' &&
    order.status === 'active' &&
    order.price !== null &&
    order.quantity !== null
      ? [
          {
            price: Number(order.price),
            side: order.side,
            title: `${order.side === 'buy' ? 'Покупка' : 'Продажа'} ${order.price} · ${remainder(order.quantity, order.filledQuantity)}`,
          },
        ]
      : [],
  );
}
