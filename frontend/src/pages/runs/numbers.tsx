import type { SetMark, SetNumbers, WeeklyNumbers } from '@/api/runs';
import { Badge, type BadgeTone } from '@/shared/ui/Badge';

import { fixed, grouped, signedPercent, signedUsdt } from './labels';
import type { SortedColumn } from './SortedTable';

/** A number of a set or of a pair; a dash for none (spec bench-run-queue «Набор без сделок»). */
const number = (value: number | string | null | undefined) =>
  value === null || value === undefined ? null : Number(value);

/** The color of a signed number: a loss red, a profit green, nothing for none. */
function Signed({ value, text }: { readonly value: number | null; readonly text: string }) {
  return (
    <span className={value === null ? 'text-faint' : value < 0 ? 'text-err' : 'text-ok'}>
      {text}
    </span>
  );
}

/** The weeks up of all the weeks with trades, «10 из 28»; a dash for none. */
export function weeksUp(weekly: WeeklyNumbers | null | undefined): string {
  return weekly === null || weekly === undefined
    ? '—'
    : `${String(weekly.weeksUp)} из ${String(weekly.weeks)}`;
}

/** The words, the tone and the place in the order of each mark (change fix-backtest-random-control, decision 14). */
const marks: Record<
  string,
  { readonly words: string; readonly tone: BadgeTone; readonly rank: number }
> = {
  passed: { words: 'прошёл', tone: 'ok', rank: 4 },
  candidate: { words: 'кандидат', tone: 'accent', rank: 3 },
  unproven: { words: 'не доказан', tone: 'warn', rank: 2 },
  failed: { words: 'не прошёл', tone: 'err', rank: 1 },
};

/** The words of a mark, or null for a set without one. */
export const markWords = (value: string | null | undefined) =>
  value === null || value === undefined ? null : (marks[value]?.words ?? value);

/** A mark as a badge; a dash for a set without one. */
export function MarkBadge({ value }: { readonly value: string | null | undefined }) {
  const known = value === null || value === undefined ? undefined : marks[value];
  return known === undefined ? (
    <span className="text-faint">—</span>
  ) : (
    <Badge tone={known.tone}>{known.words}</Badge>
  );
}

/**
 * The column of the mark of a set of ideas (spec bench-run-queue «Таблица наборов прогона»): sorted «прошёл», «кандидат»,
 * «не доказан», «не прошёл», and the sets without a mark last.
 */
export function markColumn<Row>(
  markOf: (row: Row) => SetMark | null | undefined,
): SortedColumn<Row> {
  return {
    key: 'mark',
    title: 'Отметка',
    value: (row) => {
      const value = markOf(row)?.value;
      return value === null || value === undefined ? null : (marks[value]?.rank ?? null);
    },
    cell: (row) => <MarkBadge value={markOf(row)?.value} />,
  };
}

/**
 * The columns of the numbers of a set or of a pair (spec bench-run-queue «Таблица наборов прогона»): trades, the mean
 * result of a trade, the sum in USDT, the share of wins and the open cycles, and for a strategy of ideas the difference
 * with a random entry and its t. Pairs with trades are a column of a set only, and so are the mean by weeks and the weeks
 * up of a set of ideas.
 */
export function numberColumns<Row>(
  numbersOf: (row: Row) => SetNumbers | null | undefined,
  {
    pairs,
    difference,
    weekly = false,
  }: { readonly pairs: boolean; readonly difference: boolean; readonly weekly?: boolean },
): SortedColumn<Row>[] {
  const of = (row: Row) => numbersOf(row) ?? null;
  return [
    {
      key: 'trades',
      title: 'Сделок',
      numeric: true,
      value: (row) => number(of(row)?.trades),
      cell: (row) => grouped(of(row)?.trades),
    },
    ...(pairs
      ? [
          {
            key: 'pairs',
            title: 'Пар',
            numeric: true,
            value: (row: Row) => number(of(row)?.pairsWithTrades),
            cell: (row: Row) => grouped(of(row)?.pairsWithTrades),
          },
        ]
      : []),
    {
      key: 'mean',
      title: 'Средний результат',
      numeric: true,
      value: (row) => number(of(row)?.mean),
      cell: (row) => <Signed value={number(of(row)?.mean)} text={signedPercent(of(row)?.mean)} />,
    },
    {
      key: 'amount',
      title: 'Сумма, USDT',
      numeric: true,
      value: (row) => number(of(row)?.amount),
      cell: (row) => <Signed value={number(of(row)?.amount)} text={signedUsdt(of(row)?.amount)} />,
    },
    {
      key: 'wins',
      title: 'Прибыльных',
      numeric: true,
      value: (row) => number(of(row)?.winShare),
      cell: (row) => {
        const share = number(of(row)?.winShare);
        return share === null ? '—' : `${(share * 100).toFixed(0)} %`;
      },
    },
    {
      key: 'open',
      title: 'Незакрытых',
      numeric: true,
      value: (row) => number(of(row)?.openCycles),
      cell: (row) => {
        const numbers = of(row);
        return numbers === null
          ? '—'
          : Number(numbers.openCycles) === 0
            ? '0'
            : `${String(numbers.openCycles)} · ${signedUsdt(numbers.openAmount)}`;
      },
    },
    ...(weekly
      ? [
          {
            key: 'weekly',
            title: 'По неделям',
            numeric: true,
            value: (row: Row) => number(of(row)?.weekly?.mean),
            cell: (row: Row) => (
              <Signed
                value={number(of(row)?.weekly?.mean)}
                text={signedPercent(of(row)?.weekly?.mean)}
              />
            ),
          },
          {
            key: 'weeksUp',
            title: 'Недель в плюсе',
            numeric: true,
            value: (row: Row) => {
              const weeks = of(row)?.weekly;
              return weeks === null || weeks === undefined
                ? null
                : Number(weeks.weeksUp) / Number(weeks.weeks);
            },
            cell: (row: Row) => weeksUp(of(row)?.weekly),
          },
        ]
      : []),
    ...(difference
      ? [
          {
            key: 'difference',
            title: 'Разница',
            numeric: true,
            value: (row: Row) => number(of(row)?.difference?.mean),
            cell: (row: Row) => (
              <Signed
                value={number(of(row)?.difference?.mean)}
                text={signedPercent(of(row)?.difference?.mean)}
              />
            ),
          },
          {
            key: 't',
            title: 't',
            numeric: true,
            className: 'normal-case!',
            value: (row: Row) => number(of(row)?.difference?.t),
            cell: (row: Row) => fixed(of(row)?.difference?.t, 2),
          },
        ]
      : []),
  ];
}
