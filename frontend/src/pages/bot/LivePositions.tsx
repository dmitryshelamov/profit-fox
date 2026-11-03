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

const ORDER_TYPE: Record<string, string> = { market: 'рыночный', limit: 'лимитный' };

const ORDER_COLUMNS = ['Узел', 'Сторона', 'Тип', 'Цена', 'Объём', 'Исполнено', 'Статус'];

/** The coins of the instrument: quantities are in the base coin, amounts and quote fees in the quote coin. */
interface Coins {
  readonly base: string;
  readonly quote: string;
}

/** A value with its coin; before the coins of the instrument are known, the value alone. */
function withCoin(value: string, coin: string): string {
  return `${value} ${coin}`.trimEnd();
}

function OrderHead() {
  return (
    <thead>
      <tr>
        {ORDER_COLUMNS.map((title) => (
          <th key={title} scope="col" className={head}>
            {title}
          </th>
        ))}
      </tr>
    </thead>
  );
}

/** Whether the trades of an order fall short of what it filled: they are detail and may come late or not at all. */
function shortOfTrades(order: Order): boolean {
  const traded = order.fills.reduce((sum, fill) => sum + Number(fill.quantity), 0);

  return (
    Number(order.filledQuantity) > 0 && Math.abs(traded - Number(order.filledQuantity)) > 1e-12
  );
}

/**
 * The orders with their trades under them. What an order filled, at its average price, is the truth of its fills; its trades,
 * each in the columns of its order (time, price, quantity and fee), are detail.
 */
function OrderRows({ orders, coins }: { orders: readonly Order[]; coins: Coins }) {
  return orders.map((order) => (
    <Fragment key={order.number}>
      <tr className="bg-panel-2 text-muted">
        <td className={inner}>{order.node ?? 'позиция'}</td>
        <td className={inner}>{order.side === 'buy' ? 'Покупка' : 'Продажа'}</td>
        <td className={inner}>{ORDER_TYPE[order.type] ?? order.type}</td>
        <td className={inner}>{order.price === null ? MISSING : groupDigits(order.price)}</td>
        <td className={inner}>
          {order.quantity ?? withCoin(groupDigits(order.amount ?? '0'), coins.quote)}
        </td>
        <td className={inner}>
          {order.averagePrice === null
            ? MISSING
            : `${order.filledQuantity} по ${groupDigits(order.averagePrice)}`}
          {shortOfTrades(order) && <span className="ml-2 text-faint">детализация неполная</span>}
        </td>
        <td className={inner}>
          {ORDER_STATUS[order.status] ?? order.status}
          {order.rejectReason && <span className="text-err">{` · ${order.rejectReason}`}</span>}
        </td>
      </tr>
      {order.fills.map((fill, index) => (
        <tr key={index} className="bg-panel-2 text-faint">
          <td className={`${inner} pl-8`}>{formatUtc(fill.at)}</td>
          <td className={inner} />
          <td className={inner}>сделка</td>
          <td className={inner}>{groupDigits(fill.price)}</td>
          <td className={inner}>{fill.quantity}</td>
          <td className={inner} />
          <td className={inner}>
            {withCoin(fill.fee, fill.feeAsset === 'base' ? coins.base : coins.quote)}
          </td>
        </tr>
      ))}
    </Fragment>
  ));
}

/** The current position of a live bot with its orders, then its closed positions; a closed one opens to its orders. */
export function LivePositions({
  positions,
  coins,
}: {
  positions: readonly Position[];
  coins: Coins;
}) {
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
              <OrderHead />
              <tbody>
                <OrderRows orders={current.orders} coins={coins} />
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
                  {opened.has(String(position.number)) && (
                    <OrderRows orders={position.orders} coins={coins} />
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
