import { closeReasons, type Trade, tradeStates } from '@/api/trades';
import { SideBadge } from '@/pages/ideas/ideaColumns';
import { changeText, leftText, momentText, priceWords, signTone } from '@/pages/market/format';
import { zoneLabel } from '@/shared/time';
import { Badge, type BadgeTone } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import type { PagedColumn } from '@/shared/ui/PagedTable';

import { plainPercent, signedPercent, signedUsdt } from './format';

const stateTone: Record<string, BadgeTone> = {
  pending: 'run',
  open: 'ok',
  closing: 'warn',
  error: 'err',
  closed: 'muted',
  cancelled: 'muted',
};

export function StateBadge({ state }: { readonly state: string }) {
  return <Badge tone={stateTone[state] ?? 'muted'}>{tradeStates[state] ?? state}</Badge>;
}

const closeTone: Record<string, string> = {
  target: 'text-ok',
  stop: 'text-err',
  liquidation: 'text-err',
  adl: 'text-err',
  halt: 'text-warn',
};

/** The result of an open trade now: USDT and the share of the amount. */
export function nowOf(trade: Trade) {
  if (trade.unrealised === null || trade.unrealised === undefined) {
    return <span className="text-faint">—</span>;
  }

  const percent = (Number(trade.unrealised) / Number(trade.amount)) * 100;
  return (
    <span className={`flex flex-col items-end ${signTone(trade.unrealised)}`}>
      <span>{signedUsdt(trade.unrealised)} USDT</span>
      <span className="text-xs">{signedPercent(percent)}</span>
    </span>
  );
}

const pair: PagedColumn<Trade> = {
  key: 'symbol',
  title: 'Пара',
  text: true,
  className: 'font-mono font-semibold',
  cell: (trade) => trade.symbol,
};
const account: PagedColumn<Trade> = {
  key: 'account',
  title: 'Аккаунт',
  text: true,
  className: 'text-muted',
  cell: (trade) => trade.accountName,
};
const idea: PagedColumn<Trade> = {
  key: 'strategy',
  title: 'Идея',
  text: true,
  cell: (trade) => (
    <span className="flex flex-col font-mono">
      <span>{trade.strategy}</span>
      <span className="text-xs text-muted">
        v{String(trade.version)} · {trade.timeframe}
      </span>
    </span>
  ),
};
const side: PagedColumn<Trade> = {
  key: 'side',
  title: 'Сторона',
  text: true,
  cell: (trade) => <SideBadge side={trade.side} />,
};
const amount: PagedColumn<Trade> = {
  key: 'amount',
  title: 'Сумма · плечо',
  numeric: true,
  cell: (trade) => `${Number(trade.amount)} · ${String(trade.leverage)}×`,
};

/** The columns of the open trades; `actions` draws the button of a row (spec idea-trades «Раздел „Сделки“»). */
export function openTradeColumns(
  now: number,
  onClose: (trade: Trade) => void,
  onResolve: (trade: Trade) => void,
  resolving: string | null = null,
): readonly PagedColumn<Trade>[] {
  return [
    pair,
    account,
    idea,
    side,
    amount,
    {
      key: 'entry',
      title: 'Вход',
      numeric: true,
      cell: (trade) => priceWords(trade.entryPrice),
    },
    {
      key: 'stop',
      title: 'Стоп',
      numeric: true,
      cell: (trade) => {
        const reference = Number(trade.clickPrice);
        const limit =
          trade.stopSource === 'limit' && reference > 0
            ? plainPercent((Math.abs(Number(trade.stop) - reference) / reference) * 100)
            : null;
        return (
          <span className="flex flex-col items-end">
            <span className="text-err">{priceWords(trade.stop)}</span>
            {limit !== null && (
              <span className="font-sans text-[11px] text-warn">по пределу {limit} %</span>
            )}
          </span>
        );
      },
    },
    {
      key: 'target',
      title: 'Цель',
      numeric: true,
      cell: (trade) =>
        trade.target === null || trade.target === undefined ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="text-ok">{priceWords(trade.target)}</span>
        ),
    },
    {
      key: 'left',
      title: 'До срока',
      numeric: true,
      cell: (trade) => <span className="font-sans">{leftText(Number(trade.until) - now)}</span>,
    },
    { key: 'now', title: 'Сейчас', numeric: true, cell: nowOf },
    {
      key: 'state',
      title: 'Состояние',
      text: true,
      cell: (trade) => <StateBadge state={trade.state} />,
    },
    {
      key: 'action',
      title: '',
      unsorted: true,
      cell: (trade) =>
        trade.state === 'open' ? (
          <Button
            variant="plain"
            size="sm"
            aria-label={`Закрыть ${trade.symbol}`}
            onClick={(event) => {
              event.stopPropagation();
              onClose(trade);
            }}
          >
            Закрыть
          </Button>
        ) : trade.state === 'error' ? (
          <Button
            variant="plain"
            size="sm"
            aria-label={`Разобрана ${trade.symbol}`}
            disabled={resolving === trade.id}
            onClick={(event) => {
              event.stopPropagation();
              onResolve(trade);
            }}
          >
            Разобрана
          </Button>
        ) : null,
    },
  ];
}

/** The columns of the closed and cancelled trades. */
export const closedTradeColumns: readonly PagedColumn<Trade>[] = [
  pair,
  account,
  idea,
  side,
  amount,
  {
    key: 'entryTime',
    title: `Вход, ${zoneLabel()}`,
    numeric: true,
    cell: (trade) => momentText(trade.openedAt),
  },
  {
    key: 'exitTime',
    title: `Выход, ${zoneLabel()}`,
    numeric: true,
    cell: (trade) => momentText(trade.closedAt),
  },
  {
    key: 'exitReason',
    title: 'Чем закрыта',
    text: true,
    className: 'pl-8',
    cell: (trade) =>
      trade.state === 'cancelled' ? (
        <span className="text-faint">отменена{trade.reason ? ` · ${trade.reason}` : ''}</span>
      ) : (
        <span className={closeTone[trade.closeReason ?? ''] ?? 'text-muted'}>
          {closeReasons[trade.closeReason ?? ''] ?? '—'}
        </span>
      ),
  },
  {
    key: 'fees',
    title: 'Комиссии',
    numeric: true,
    cell: (trade) => (trade.state === 'cancelled' ? '—' : Number(trade.fees).toFixed(2)),
  },
  {
    key: 'result',
    title: 'Итог, USDT',
    numeric: true,
    cell: (trade) => <span className={signTone(trade.result)}>{signedUsdt(trade.result)}</span>,
  },
  {
    key: 'share',
    title: 'Итог',
    numeric: true,
    cell: (trade) => (
      <span className={signTone(trade.resultShare)}>{changeText(trade.resultShare, 1)}</span>
    ),
  },
  {
    key: 'ideaResult',
    title: 'Итог идеи',
    numeric: true,
    cell: (trade) => (
      <span className={signTone(trade.ideaResult)}>{changeText(trade.ideaResult)}</span>
    ),
  },
];
