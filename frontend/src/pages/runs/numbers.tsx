import type { SetNumbers } from '@/api/runs';

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

/**
 * The columns of the numbers of a set or of a pair (spec bench-run-queue «Таблица наборов прогона»): trades, the mean
 * result of a trade, the sum in USDT, the share of wins and the open cycles, and for a strategy of ideas the difference
 * with a random entry and its t. Pairs with trades are a column of a set only.
 */
export function numberColumns<Row>(
  numbersOf: (row: Row) => SetNumbers | null | undefined,
  { pairs, difference }: { readonly pairs: boolean; readonly difference: boolean },
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
