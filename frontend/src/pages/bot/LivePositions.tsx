import { Fragment, useState } from 'react';

import type { Schemas } from '@/shared/api/client';
import { formatUtc, groupDigits, showDecimal } from '@/shared/format/format';

type Position = Schemas['LivePositionResponse'];
type Order = Schemas['LiveOrderResponse'];

const head = 'px-4 py-3 text-left text-[11px] font-semibold tracking-wide text-faint uppercase';
const cell = 'px-4 py-2.5 font-mono whitespace-nowrap';
const inner = 'px-4 py-1.5 font-mono text-xs whitespace-nowrap';
const term = 'text-[11px] tracking-wide text-faint uppercase';
const MISSING = '—';

const STATUS: Record<string, string> = {
  waiting: 'ждёт входа',
  open: 'открыта',
  closing: 'закрывается',
  closed: 'закрыта',
};

const ORDER_STATUS: Record<string, string> = {
  pending_submit: 'отправляется',
  active: 'активен',
  pending_cancel: 'отменяется',
  cancel_sent: 'отменяется',
  filled: 'исполнен',
  cancelled: 'отменён',
  rejected: 'отклонён',
};

function OrderRows({ orders }: { orders: readonly Order[] }) {
  return orders.map((order) => (
    <Fragment key={order.number}>
      <tr className="bg-panel-2 text-muted">
        <td className={inner}>{order.node ?? 'позиция'}</td>
        <td className={inner}>{order.side === 'buy' ? 'Покупка' : 'Продажа'}</td>
        <td className={inner}>{order.type}</td>
        <td className={inner}>{order.price === null ? MISSING : groupDigits(order.price)}</td>
        <td className={inner}>{order.quantity ?? order.amount}</td>
        <td className={inner} title={order.rejectReason ?? undefined}>
          {ORDER_STATUS[order.status] ?? order.status}
        </td>
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

/** The current position of a live bot with its orders, then its closed positions; a closed one opens to its orders. */
export function LivePositions({ positions }: { positions: readonly Position[] }) {
  const [opened, setOpened] = useState<ReadonlySet<string>>(new Set());
  const current = positions.find((position) => position.status !== 'closed');
  const closed = positions.filter((position) => position.status === 'closed');
  const toggle = (number: string) =>
    setOpened((now) => {
      const next = new Set(now);

      if (!next.delete(number)) {
        next.add(number);
      }

      return next;
    });

  return (
    <div className="space-y-6">
      {current && (
        <section
          aria-label="Текущая позиция"
          className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
        >
          <h2 className="text-sm font-bold">{`Позиция ${current.number}`}</h2>
          <dl className="grid grid-cols-3 gap-x-8 gap-y-3 text-sm">
            <div>
              <dt className={term}>Состояние</dt>
              <dd>{STATUS[current.status]}</dd>
            </div>
            <div>
              <dt className={term}>Объём</dt>
              <dd className="font-mono">{current.quantity}</dd>
            </div>
            <div>
              <dt className={term}>Средняя цена</dt>
              <dd className="font-mono">
                {current.entryPrice === null ? MISSING : groupDigits(current.entryPrice)}
              </dd>
            </div>
            <div>
              <dt className={term}>Вложено</dt>
              <dd className="font-mono">{groupDigits(current.invested)}</dd>
            </div>
            <div>
              <dt className={term}>PnL</dt>
              <dd className="font-mono">{showDecimal(current.pnl, 2, true)}</dd>
            </div>
            <div>
              <dt className={term}>Комиссии</dt>
              <dd className="font-mono">{showDecimal(current.fees, 2)}</dd>
            </div>
          </dl>
          {current.orders.length > 0 && (
            <table className="w-full border-separate border-spacing-y-1 text-sm">
              <tbody>
                <OrderRows orders={current.orders} />
              </tbody>
            </table>
          )}
        </section>
      )}
      {closed.length > 0 && (
        <section aria-label="Закрытые позиции" className="space-y-3">
          <h2 className="text-sm font-bold">{`Закрытые позиции · ${closed.length}`}</h2>
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead className="bg-panel">
              <tr>
                {['№', 'Открытие', 'Закрытие', 'Вход', 'Выход', 'PnL', 'Комиссии', ''].map(
                  (title, index) => (
                    <th key={index} scope="col" className={head}>
                      {title}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {closed.map((position) => (
                <Fragment key={position.number}>
                  <tr className="bg-panel">
                    <td className={cell}>{position.number}</td>
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
                    <td className={cell}>{showDecimal(position.pnl, 2, true)}</td>
                    <td className={cell}>{showDecimal(position.fees, 2)}</td>
                    <td className={cell}>
                      <button
                        type="button"
                        aria-expanded={opened.has(String(position.number))}
                        aria-label={`Ордера позиции ${position.number}`}
                        className="rounded-md px-2 text-muted hover:bg-panel-2"
                        onClick={() => toggle(String(position.number))}
                      >
                        {opened.has(String(position.number)) ? '▴' : '▾'}
                      </button>
                    </td>
                  </tr>
                  {opened.has(String(position.number)) && <OrderRows orders={position.orders} />}
                </Fragment>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
