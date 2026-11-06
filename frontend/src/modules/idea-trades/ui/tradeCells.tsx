import { sideLabel, typeLabel } from '@/modules/ideas';
import { CategoryBadge, setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import type { Schemas } from '@/shared/api/client';

import { statusLabel } from '../model/status';

export type IdeaTrade = Schemas['IdeaTradeResponse'];

export const cell = 'px-3 py-3';
export const number = `${cell} text-right font-mono whitespace-nowrap`;
export const head = 'bg-panel text-left text-[11px] tracking-wide text-faint uppercase';

export function Heads({ names, right }: { names: readonly string[]; right: readonly string[] }) {
  return (
    <>
      {names.map((label) => (
        <th key={label} scope="col" className={right.includes(label) ? `${cell} text-right` : cell}>
          {label}
        </th>
      ))}
    </>
  );
}

/** The pair of a trade with its market, then its setup with the interval, its type and its side. */
export function InstrumentCells({ trade }: { trade: IdeaTrade }) {
  return (
    <>
      <td className={`${cell} whitespace-nowrap`}>
        <CategoryBadge category={trade.category} />{' '}
        <span className="ml-1.5 font-semibold">{trade.symbol}</span>
      </td>
      <td className={cell}>
        {setupLabel(trade.setup)} · {intervalLabel(trade.interval)}
        <span className="block text-xs whitespace-nowrap text-muted">{typeLabel(trade.type)}</span>
      </td>
      <td className={cell}>{sideLabel(trade.side)}</td>
    </>
  );
}

/** The entry, stop, target, quantity and — for the open trades — the risk of a trade. */
export function LevelCells({ trade, risk }: { trade: IdeaTrade; risk: boolean }) {
  return (
    <>
      <td className={number}>{trade.filledPrice ?? trade.entryPrice}</td>
      <td className={number}>{trade.stop}</td>
      <td className={number}>{trade.target}</td>
      <td className={number}>{trade.quantity}</td>
      {risk && <td className={number}>{trade.risk}</td>}
    </>
  );
}

/** The status of a trade and, under it, the reason it carries. */
export function StatusCell({ trade }: { trade: IdeaTrade }) {
  return (
    <td className={cell}>
      <span>{statusLabel(trade.status)}</span>
      {trade.reason !== null && (
        <span
          className={`mt-1 block max-w-xs text-xs ${trade.status === 'failed' ? 'text-err' : 'text-warn'}`}
        >
          {trade.reason}
        </span>
      )}
    </td>
  );
}
