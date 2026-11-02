import type { Schemas } from '@/shared/api/client';
import { showDecimal, signed } from '@/shared/format/format';

type Metrics = Schemas['MetricsResponse'];

export interface ResultColumn {
  /** The metric code the API sorts by. */
  readonly code: string;
  readonly title: string;
  /** The order the best results come in, which a first click on the column gives. */
  readonly best: 'asc' | 'desc';
  readonly show: (metrics: Metrics) => string;
  /** The value as the API gave it, for a tooltip over the rounded one. */
  readonly exact: (metrics: Metrics) => string;
  readonly tone?: (metrics: Metrics) => 'ok' | 'err' | undefined;
}

const MISSING = '—';
const percent = (value: string | null) => (value === null ? MISSING : `${showDecimal(value, 2)} %`);
const exactOf = (value: string | null) => value ?? MISSING;
const toneOf = (value: string) =>
  value.startsWith('-') ? 'err' : signed(value).startsWith('+') ? 'ok' : undefined;

/** The metrics of a result the table of results and the comparison show, in this order. */
export const resultColumns: readonly ResultColumn[] = [
  {
    code: 'return',
    best: 'desc',
    title: 'Доходность',
    show: (metrics) => `${showDecimal(metrics.return, 2, true)} %`,
    exact: (metrics) => metrics.return,
    tone: (metrics) => toneOf(metrics.return),
  },
  {
    code: 'maxDrawdown',
    best: 'asc',
    title: 'Макс. просадка',
    show: (metrics) => percent(metrics.maxDrawdown),
    exact: (metrics) => exactOf(metrics.maxDrawdown),
  },
  {
    code: 'returnToDrawdown',
    best: 'desc',
    title: 'Дох. / просадка',
    show: (metrics) =>
      metrics.returnToDrawdown === null ? MISSING : showDecimal(metrics.returnToDrawdown, 2),
    exact: (metrics) => exactOf(metrics.returnToDrawdown),
  },
  {
    code: 'closedTrades',
    best: 'desc',
    title: 'Сделки',
    show: (metrics) => String(metrics.closedTrades),
    exact: (metrics) => String(metrics.closedTrades),
  },
  {
    code: 'winRate',
    best: 'desc',
    title: 'Прибыльных',
    show: (metrics) => percent(metrics.winRate),
    exact: (metrics) => exactOf(metrics.winRate),
  },
  {
    code: 'fees',
    best: 'asc',
    title: 'Комиссии',
    show: (metrics) => showDecimal(metrics.fees, 2),
    exact: (metrics) => metrics.fees,
  },
  {
    code: 'pnl',
    best: 'desc',
    title: 'PnL',
    show: (metrics) => showDecimal(metrics.pnl, 2, true),
    exact: (metrics) => metrics.pnl,
    tone: (metrics) => toneOf(metrics.pnl),
  },
];
