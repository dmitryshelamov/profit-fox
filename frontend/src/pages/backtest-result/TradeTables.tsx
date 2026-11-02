import { useState } from 'react';

import type { Schemas } from '@/shared/api/client';
import { formatUtc, groupDigits, showDecimal, signed } from '@/shared/format/format';

const head = 'px-4 py-3 text-left text-[11px] font-semibold tracking-wide text-faint uppercase';
const cell = 'px-4 py-2.5 font-mono whitespace-nowrap';
const MISSING = '—';

const toneOf = (value: string) =>
  value.startsWith('-') ? 'text-err' : signed(value).startsWith('+') ? 'text-ok' : '';

/** The trades and the fills of a result, one table at a time. */
export function TradeTables({
  trades,
  fills,
}: {
  trades: readonly Schemas['TradeResponse'][];
  fills: readonly Schemas['FillResponse'][];
}) {
  const [tab, setTab] = useState<'trades' | 'fills'>('trades');
  const tabs = [
    { id: 'trades', title: `Сделки · ${trades.length}` },
    { id: 'fills', title: `Исполнения · ${fills.length}` },
  ] as const;

  return (
    <section className="space-y-3">
      <div
        role="tablist"
        className="inline-flex gap-1 rounded-lg border border-line bg-panel p-1 text-sm"
      >
        {tabs.map(({ id, title }) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            className={`h-8 rounded-md px-4 ${tab === id ? 'bg-panel-2 font-bold' : 'text-muted'}`}
            onClick={() => setTab(id)}
          >
            {title}
          </button>
        ))}
      </div>
      {tab === 'trades' ? (
        <table className="w-full border-separate border-spacing-y-1 text-sm">
          <thead className="bg-panel">
            <tr>
              {['Открытие', 'Закрытие', 'Вход', 'Выход', 'PnL', 'Комиссии'].map((title) => (
                <th key={title} scope="col" className={head}>
                  {title}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {trades.map((trade) => (
              <tr key={trade.openedAt} className="bg-panel">
                <td className={cell}>{formatUtc(trade.openedAt)}</td>
                <td className={cell}>
                  {trade.closedAt === null ? (
                    <span className="text-warn">открыта</span>
                  ) : (
                    formatUtc(trade.closedAt)
                  )}
                </td>
                <td className={cell}>{groupDigits(trade.entryPrice)}</td>
                <td className={cell}>
                  {trade.exitPrice === null ? MISSING : groupDigits(trade.exitPrice)}
                </td>
                <td
                  className={`${cell} ${trade.pnl === null ? '' : toneOf(trade.pnl)}`}
                  title={trade.pnl ?? undefined}
                >
                  {trade.pnl === null ? MISSING : showDecimal(trade.pnl, 2, true)}
                </td>
                <td className={cell} title={trade.fees}>
                  {showDecimal(trade.fees, 2)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <table className="w-full border-separate border-spacing-y-1 text-sm">
          <thead className="bg-panel">
            <tr>
              {['Время', 'Сторона', 'Тип', 'Цена', 'Объём', 'Комиссия'].map((title) => (
                <th key={title} scope="col" className={head}>
                  {title}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {fills.map((fill, index) => (
              <tr key={index} className="bg-panel">
                <td className={cell}>{formatUtc(fill.at)}</td>
                <td className={cell}>{fill.side === 'buy' ? 'Покупка' : 'Продажа'}</td>
                <td className={cell}>{fill.type}</td>
                <td className={cell}>{groupDigits(fill.price)}</td>
                <td className={cell}>{fill.quantity}</td>
                <td className={cell}>
                  {fill.fee} {fill.feeAsset}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
