import { IdeaFeatureChips, sideLabel, typeLabel } from '@/modules/ideas';
import { CategoryBadge, setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import type { Schemas } from '@/shared/api/client';
import { roundDecimal } from '@/shared/format/format';

import { statusLabel } from '../model/status';

export type IdeaTrade = Schemas['IdeaTradeResponse'];

export const cell = 'px-2 py-3';
export const number = `${cell} text-right font-mono whitespace-nowrap`;
export const head = 'bg-panel text-left text-[11px] tracking-wide text-faint uppercase';

/** A text of parts joined by « · » for a narrow column: it wraps only between the parts, a part stays on one line. */
export function byParts(text: string): string {
  return text
    .split(' · ')
    .map((part) => part.replaceAll(' ', '\u00a0'))
    .join(' · ');
}

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

/** The pair of a trade with its market, then its setup with the interval, its type and the chips of its features, and its side. */
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
        <IdeaFeatureChips features={trade.features} />
      </td>
      <td className={cell}>{sideLabel(trade.side)}</td>
    </>
  );
}

/**
 * The entry, stop, target, quantity and — for the open trades — the risk of a trade. The average of the fills is shown with
 * the decimals of the entry, the risk in cents.
 */
export function LevelCells({ trade, risk }: { trade: IdeaTrade; risk: boolean }) {
  const entryDigits = (trade.entryPrice.split('.')[1] ?? '').length;

  return (
    <>
      <td className={number}>
        {trade.filledPrice === null
          ? trade.entryPrice
          : roundDecimal(trade.filledPrice, entryDigits)}
      </td>
      <td className={number}>{trade.stop}</td>
      <td className={number}>{trade.target}</td>
      <td className={number}>{trade.quantity}</td>
      {risk && <td className={number}>{roundDecimal(trade.risk, 2)}</td>}
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

/** The class of the first cell of a paper trade: a stripe on the left that marks it among the real ones. */
export const stripeOf = (trade: IdeaTrade) =>
  trade.origin === 'paper' ? 'border-l-[3px] border-run' : '';

/** Who took the trade: the owner by hand, an auto with money (its name under the mark) or an auto without money. */
export function OriginCell({ trade }: { trade: IdeaTrade }) {
  if (trade.origin === 'manual') {
    return (
      <td className={cell}>
        <span className="rounded bg-panel-2 px-1.5 py-px text-xs font-semibold">Ручная</span>
      </td>
    );
  }

  return (
    <td className={cell}>
      <span
        className={`rounded border px-1.5 py-px text-xs font-semibold ${trade.origin === 'paper' ? 'border-run text-run' : 'border-accent text-accent'}`}
      >
        {trade.origin === 'paper' ? 'учебная' : 'Авто'}
      </span>
      <span className="mt-1 block text-xs whitespace-nowrap text-muted">{trade.autoName}</span>
    </td>
  );
}
