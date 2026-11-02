import { Fragment, useState } from 'react';

import type { Schemas } from '@/shared/api/client';
import { formatUtc, groupDigits, showDecimal, signed } from '@/shared/format/format';

const head = 'px-4 py-3 text-left text-[11px] font-semibold tracking-wide text-faint uppercase';
const cell = 'px-4 py-2.5 font-mono whitespace-nowrap';
const inner = 'px-4 py-1.5 font-mono text-xs whitespace-nowrap';
const MISSING = '—';

const STATUS: Record<string, string> = {
  waiting: 'ждёт входа',
  open: 'открыта',
  closing: 'закрывается',
  closed: 'закрыта',
};

const ORDER_STATUS: Record<string, string> = { filled: 'исполнен', active: 'активен' };

const toneOf = (value: string) =>
  value.startsWith('-') ? 'text-err' : signed(value).startsWith('+') ? 'text-ok' : '';

/** The orders of a position with their fills, under its row. */
function OrderRows({ orders }: { orders: readonly Schemas['OrderResponse'][] }) {
  return orders.map((order) => (
    <Fragment key={order.id}>
      <tr className="bg-panel-2 text-muted">
        <td className={inner}>{order.node ?? 'позиция'}</td>
        <td className={inner}>{order.side === 'buy' ? 'Покупка' : 'Продажа'}</td>
        <td className={inner}>{order.type}</td>
        <td className={inner}>{order.price === null ? MISSING : groupDigits(order.price)}</td>
        <td className={inner}>{order.quantity ?? order.amount}</td>
        <td className={inner}>{ORDER_STATUS[order.status]}</td>
      </tr>
      {order.fills.map((fill, index) => (
        <tr key={index} className="bg-panel-2 text-faint">
          <td className={`${inner} pl-8`}>{formatUtc(fill.at)}</td>
          <td className={inner}>{groupDigits(fill.price)}</td>
          <td className={inner}>{fill.quantity}</td>
          <td className={inner}>
            {fill.fee} {fill.feeAsset}
          </td>
        </tr>
      ))}
    </Fragment>
  ));
}

/** The positions of a result; a row opens to the orders of its position and their fills. */
export function PositionsTable({
  positions,
}: {
  positions: readonly Schemas['PositionResponse'][];
}) {
  const [opened, setOpened] = useState<ReadonlySet<string>>(new Set());
  const toggle = (number: string) =>
    setOpened((current) => {
      const next = new Set(current);

      if (!next.delete(number)) {
        next.add(number);
      }

      return next;
    });

  return (
    <section className="space-y-3">
      <h2 className="text-sm font-bold">{`Позиции · ${positions.length}`}</h2>
      <table className="w-full border-separate border-spacing-y-1 text-sm">
        <thead className="bg-panel">
          <tr>
            {['№', 'Состояние', 'Открытие', 'Закрытие', 'Вход', 'Выход', 'PnL', 'Комиссии', ''].map(
              (title, index) => (
                <th key={index} scope="col" className={head}>
                  {title}
                </th>
              ),
            )}
          </tr>
        </thead>
        <tbody>
          {positions.map((position) => {
            const closed = position.status === 'closed';
            const number = String(position.number);
            const expanded = opened.has(number);

            return (
              <Fragment key={position.number}>
                <tr className="bg-panel">
                  <td className={cell}>{position.number}</td>
                  <td className={cell}>
                    <span className={closed ? '' : 'text-warn'}>{STATUS[position.status]}</span>
                    {Number(position.carriedQuantity) !== 0 && (
                      <span className="ml-2 text-xs text-faint">{`перенесено ${position.carriedQuantity}`}</span>
                    )}
                  </td>
                  <td className={cell}>
                    {position.openedAt === null ? MISSING : formatUtc(position.openedAt)}
                  </td>
                  <td className={cell}>
                    {position.closedAt === null ? MISSING : formatUtc(position.closedAt)}
                  </td>
                  <td className={cell}>
                    {position.entryPrice === null ? MISSING : groupDigits(position.entryPrice)}
                  </td>
                  <td className={cell}>
                    {position.exitPrice === null ? MISSING : groupDigits(position.exitPrice)}
                  </td>
                  <td
                    className={`${cell} ${closed ? toneOf(position.pnl) : ''}`}
                    title={closed ? position.pnl : undefined}
                  >
                    {closed ? showDecimal(position.pnl, 2, true) : MISSING}
                  </td>
                  <td className={cell} title={position.fees}>
                    {showDecimal(position.fees, 2)}
                  </td>
                  <td className={cell}>
                    <button
                      type="button"
                      aria-expanded={expanded}
                      aria-label={`Ордера позиции ${position.number}`}
                      className="rounded-md px-2 text-muted hover:bg-panel-2"
                      onClick={() => toggle(number)}
                    >
                      {expanded ? '▴' : '▾'}
                    </button>
                  </td>
                </tr>
                {expanded && <OrderRows orders={position.orders} />}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}
