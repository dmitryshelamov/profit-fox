import type { ReactNode } from 'react';

import { groupDigits } from '@/shared/format/format';

import { compactNumber, fundingText, spreadText } from '../model/numbers';
import type { MarketRow } from '../model/rows';
import type { SortColumn } from '../model/sorting';
import { PercentChange } from './PercentChange';

/** A value through a formatter; an empty one is a dash. */
export const shown = (value: string | null, format: (value: string) => string): ReactNode =>
  value === null ? '—' : format(value);

export interface MetricColumn {
  readonly column: Exclude<SortColumn, 'symbol'>;
  /** The header under its group, or of its own when the column has no group. */
  readonly title: string;
  /** What the header sorts by, for a screen reader: «Изменение за 1ч». */
  readonly label: string;
  readonly group?: string;
  readonly render: (row: MarketRow) => ReactNode;
}

const change = (column: MetricColumn['column'], title: string): MetricColumn => ({
  column,
  title,
  label: `Изменение за ${title}`,
  group: 'Изменение, %',
  render: (row) => <PercentChange value={row[column]} />,
});

/** The metrics of a pair after its symbol, in the order of the table, grouped as in the mockup; the cells carry no unit. */
export const metricColumns: readonly MetricColumn[] = [
  { column: 'price', title: 'Цена', label: 'Цена', render: (row) => shown(row.price, groupDigits) },
  change('change5m', '5м'),
  change('change15m', '15м'),
  change('change1h', '1ч'),
  change('change4h', '4ч'),
  change('change24h', '24ч'),
  {
    column: 'turnover1h',
    title: '1ч',
    label: 'Оборот за 1ч',
    group: 'Оборот, USDT',
    render: (row) => shown(row.turnover1h, compactNumber),
  },
  {
    column: 'turnover24h',
    title: '24ч',
    label: 'Оборот за 24ч',
    group: 'Оборот, USDT',
    render: (row) => shown(row.turnover24h, compactNumber),
  },
  {
    column: 'openInterest',
    title: 'OI, USDT',
    label: 'OI, USDT',
    render: (row) => shown(row.openInterest, compactNumber),
  },
  {
    column: 'openInterestChange1h',
    title: '1ч',
    label: 'ΔOI за 1ч',
    group: 'ΔOI, %',
    render: (row) => <PercentChange value={row.openInterestChange1h} digits={1} />,
  },
  {
    column: 'openInterestChange24h',
    title: '24ч',
    label: 'ΔOI за 24ч',
    group: 'ΔOI, %',
    render: (row) => <PercentChange value={row.openInterestChange24h} digits={1} />,
  },
  {
    column: 'fundingRate',
    title: 'Фандинг, %',
    label: 'Фандинг, %',
    render: (row) => shown(row.fundingRate, fundingText),
  },
  {
    column: 'spread',
    title: 'Спред, %',
    label: 'Спред, %',
    render: (row) => shown(row.spread, spreadText),
  },
];
