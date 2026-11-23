import type { Idea, IdeaSummary } from '@/api/ideas';
import {
  changeText,
  leftText,
  momentText,
  priceWords,
  signedAmount,
  signTone,
} from '@/pages/market/format';
import { Badge } from '@/shared/ui/Badge';
import type { PagedColumn } from '@/shared/ui/PagedTable';

import { LabelBadges } from './LabelBadges';

/** The words of the reason an idea closed. */
export const exitWords: Record<string, string> = {
  stop: 'по стопу',
  target: 'по цели',
  time: 'по сроку',
};

const exitTone: Record<string, string> = {
  stop: 'text-err',
  target: 'text-ok',
  time: 'text-muted',
};

export function SideBadge({ side }: { readonly side: string }) {
  return <Badge tone={side === 'short' ? 'err' : 'ok'}>{side === 'short' ? 'шорт' : 'лонг'}</Badge>;
}

// The name stays on one line (task 19.15): a long one is cut with an ellipsis and has the whole name as a hint; the
// version beside it is never cut.
function Strategy({ name, version }: { readonly name: string; readonly version: number | string }) {
  return (
    <span className="flex min-w-0 items-center gap-2 font-mono">
      <span title={name} className="max-w-[200px] min-w-0 truncate">
        {name}
      </span>
      <Badge mono>v{String(version)}</Badge>
    </span>
  );
}

// The longest symbols («1000000BABYDOGEUSDT») are cut with an ellipsis and have the whole as a hint: with them the table of
// the open ideas is wider than its frame even with the column of the strategy as narrow as its head (task 20.8).
const symbol: PagedColumn<Idea> = {
  key: 'symbol',
  title: 'Пара',
  text: true,
  className: 'font-mono font-semibold',
  cell: (idea) => (
    <span title={idea.symbol} className="block max-w-[120px] truncate">
      {idea.symbol}
    </span>
  ),
};
// The column of the strategy gives way when the table is wider than its frame (task 20.8): its cells take what the other
// columns leave and count for nothing in the width of the table, the name cut with its ellipsis; the head keeps it no
// narrower than its title.
const strategy: PagedColumn<Idea> = {
  key: 'strategy',
  title: 'Стратегия',
  text: true,
  cellClassName: 'w-full max-w-0',
  cell: (idea) => <Strategy name={idea.strategy} version={idea.version} />,
};
const timeframe: PagedColumn<Idea> = {
  key: 'timeframe',
  title: 'ТФ',
  className: 'font-mono text-muted',
  cell: (idea) => idea.timeframe,
};
const side: PagedColumn<Idea> = {
  key: 'side',
  title: 'Сторона',
  text: true,
  cell: (idea) => <SideBadge side={idea.side} />,
};
const labels: PagedColumn<Idea> = {
  key: 'labels',
  title: 'Метки',
  text: true,
  cell: (idea) => <LabelBadges labels={idea.labels} />,
};
const price = (key: 'stop' | 'target', title: string, tone: string): PagedColumn<Idea> => ({
  key,
  title,
  numeric: true,
  cell: (idea) =>
    idea[key] === null || idea[key] === undefined ? (
      <span className="text-faint">—</span>
    ) : (
      <span className={tone}>{priceWords(idea[key])}</span>
    ),
});
const entry: PagedColumn<Idea> = {
  key: 'entry',
  title: 'Вход',
  numeric: true,
  cell: (idea) => priceWords(idea.entry),
};

/** What is left of the term of an open idea, from now. */
export function leftOf(idea: Idea, now = Date.now()): string {
  return leftText(Number(idea.until) - now);
}

/** The result of an idea: «сейчас» while it is open, the outcome once closed. */
export function resultOf(idea: Idea) {
  const value = idea.state === 'open' ? idea.now : idea.result;
  return <span className={signTone(value)}>{changeText(value)}</span>;
}

/** The columns of the open ideas (spec strategy-ideas «Раздел «Идеи»»). */
export const openColumns: readonly PagedColumn<Idea>[] = [
  symbol,
  strategy,
  timeframe,
  side,
  labels,
  {
    key: 'moment',
    title: 'Появилась, UTC',
    numeric: true,
    cell: (idea) => momentText(idea.moment),
  },
  entry,
  price('stop', 'Стоп', 'text-err'),
  price('target', 'Цель', 'text-ok'),
  {
    key: 'left',
    title: 'Осталось',
    numeric: true,
    cell: (idea) => <span className="font-sans">{leftOf(idea)}</span>,
  },
  { key: 'now', title: 'Сейчас', numeric: true, cell: resultOf },
];

/** The columns of the closed ideas of the journal. */
export const closedColumns: readonly PagedColumn<Idea>[] = [
  symbol,
  strategy,
  timeframe,
  side,
  labels,
  {
    key: 'moment',
    title: 'Вход, UTC',
    numeric: true,
    cell: (idea) => momentText(idea.entryTime ?? idea.moment),
  },
  {
    key: 'exitTime',
    title: 'Выход, UTC',
    numeric: true,
    cell: (idea) => momentText(idea.exitTime),
  },
  {
    key: 'exitReason',
    title: 'Чем закрыта',
    text: true,
    className: 'pl-10',
    cell: (idea) => (
      <span className={exitTone[idea.exitReason ?? ''] ?? 'text-muted'}>
        {exitWords[idea.exitReason ?? ''] ?? '—'}
      </span>
    ),
  },
  { key: 'result', title: 'Итог', numeric: true, cell: resultOf },
];

/** The columns of the ideas of a pair: open and closed together (spec market-overview «Идеи пары»). */
export const pairColumns: readonly PagedColumn<Idea>[] = [
  strategy,
  timeframe,
  side,
  labels,
  {
    key: 'moment',
    title: 'Вход, UTC',
    numeric: true,
    cell: (idea) => momentText(idea.entryTime ?? idea.moment),
  },
  entry,
  price('stop', 'Стоп', 'text-err'),
  price('target', 'Цель', 'text-ok'),
  {
    key: 'exitTime',
    title: 'Выход',
    numeric: true,
    cell: (idea) =>
      idea.state === 'open' ? (
        <span className="font-sans text-run">открыта · {leftOf(idea)}</span>
      ) : idea.state === 'excluded' ? (
        <span className="font-sans text-faint">исключена</span>
      ) : (
        momentText(idea.exitTime)
      ),
  },
  {
    key: 'exitReason',
    title: 'Чем закрыта',
    text: true,
    className: 'pl-8',
    cell: (idea) => (
      <span className={exitTone[idea.exitReason ?? ''] ?? 'text-faint'}>
        {exitWords[idea.exitReason ?? ''] ?? '—'}
      </span>
    ),
  },
  { key: 'result', title: 'Итог', numeric: true, cell: resultOf },
];

/** The columns of the summary of the journal. */
export const summaryColumns: readonly PagedColumn<IdeaSummary>[] = [
  {
    key: 'strategy',
    title: 'Стратегия',
    text: true,
    cell: (row) => <Strategy name={row.strategy} version={row.version} />,
  },
  {
    key: 'timeframe',
    title: 'ТФ',
    className: 'font-mono text-muted',
    cell: (row) => row.timeframe,
  },
  { key: 'ideas', title: 'Идей', numeric: true, cell: (row) => String(row.ideas) },
  {
    key: 'wins',
    title: 'Прибыльных',
    numeric: true,
    cell: (row) => `${Math.round(Number(row.wins) * 100)} %`,
  },
  {
    key: 'mean',
    title: 'Средний итог',
    numeric: true,
    cell: (row) => <span className={signTone(row.mean)}>{changeText(row.mean)}</span>,
  },
  {
    key: 'sum',
    title: 'Сумма, USDT',
    numeric: true,
    cell: (row) => <span className={signTone(row.sum)}>{signedAmount(row.sum)}</span>,
  },
];
