import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';

import type { Halt, RiskState } from '@/api/risk';
import { momentText, signTone } from '@/pages/market/format';
import { amountWords, lossWords, plural, signedUsdt } from '@/pages/trades/format';

function Cell({
  title,
  ratio,
  tone,
  children,
}: {
  readonly title: string;
  readonly ratio?: number;
  readonly tone?: string;
  readonly children: ReactNode;
}) {
  return (
    <div className="flex min-w-0 flex-1 flex-col gap-1.5 px-5">
      <span className="text-[11px] font-semibold text-muted uppercase">{title}</span>
      <span className="font-mono text-base font-semibold">{children}</span>
      {ratio !== undefined && (
        <span aria-hidden="true" className="h-1 rounded-full bg-chip">
          <span
            className={`block h-full rounded-full ${tone ?? 'bg-run'}`}
            style={{ width: `${Math.min(100, Math.max(0, ratio * 100))}%` }}
          />
        </span>
      )}
    </div>
  );
}

const share = (value: number, limit: number | string | null | undefined) =>
  limit === null || limit === undefined || Number(limit) <= 0 ? 0 : value / Number(limit);

/**
 * The state of the risk module (spec trade-risk «Состояние риска», «Полоса риска»): the loss of the day, the open trades
 * and the sum in trades, each beside its limit, and the trading Worker with the streams of Bybit. «Сделки» gives `closed`:
 * the net outcome of all the closed trades — fees taken off, funding in — with the fees in brackets, beside the loss.
 */
export function RiskStrip({
  state,
  closed,
}: {
  readonly state: RiskState;
  readonly closed?: { readonly result: number | string; readonly fees: number | string };
}) {
  const dayLoss = Number(state.dayLoss);
  const streamed = Number(state.streamed);
  return (
    <section
      aria-label="Состояние риска"
      className="flex divide-x divide-line rounded-[10px] border border-line bg-panel py-4"
    >
      <Cell title="Убыток за день · сутки UTC" ratio={share(dayLoss, state.dayLimit)} tone="bg-err">
        <span data-testid="day-loss">
          {lossWords(dayLoss)} из {lossWords(state.dayLimit)} USDT
        </span>
      </Cell>
      {closed !== undefined && (
        <Cell title="Итог закрытых · чистыми">
          <span data-testid="closed-result">
            <span className={signTone(closed.result)}>{signedUsdt(closed.result)} USDT</span>{' '}
            <span className="text-sm font-normal text-muted">
              (комиссия {Number(closed.fees).toFixed(2)})
            </span>
          </span>
        </Cell>
      )}
      <Cell title="Открытые сделки" ratio={share(Number(state.openTrades), state.maxOpenTrades)}>
        {String(state.openTrades)} из {String(state.maxOpenTrades ?? '—')}
      </Cell>
      <Cell title="В сделках" ratio={share(Number(state.openAmount), state.maxTotalAmount)}>
        {amountWords(state.openAmount)} из {amountWords(state.maxTotalAmount)} USDT
      </Cell>
      <Cell title="Торговый Worker">
        <span className="flex items-center gap-2 font-sans">
          <span
            aria-hidden="true"
            className={`size-2 rounded-full ${state.traderAlive ? 'bg-ok' : 'bg-err'}`}
          />
          {state.traderAlive ? 'работает' : 'не работает'}
        </span>
        <span className="font-sans text-xs font-normal text-muted">
          Bybit: поток · {streamed} {plural(streamed, ['аккаунт', 'аккаунта', 'аккаунтов'])}
        </span>
      </Cell>
    </section>
  );
}

/**
 * The emergency stop on the screen (spec trade-risk «Стоп-кран на экране»): the moment, the loss and the limit, the pairs
 * it did not close. «Риск» puts its button in `children`; «Сделки» a link to «Риск».
 */
export function HaltBanner({
  halt,
  children,
}: {
  readonly halt: Halt;
  readonly children?: ReactNode;
}) {
  const notClosed = halt.notClosed;
  return (
    <section
      aria-label="Баннер стоп-крана"
      className="flex items-center justify-between gap-6 rounded-[10px] border border-err bg-err-bg px-6 py-5"
    >
      <div className="flex flex-col gap-1.5">
        <h2 className="text-base font-bold text-err">Стоп-кран · {momentText(halt.haltedAt)}</h2>
        <p>
          Убыток за день {Number(halt.loss).toFixed(2)} USDT при пределе{' '}
          {Number(halt.limit).toFixed(2)}. Все сделки закрыты по рынку, вход в идеи запрещён.
        </p>
        <p className="text-xs text-muted">
          {notClosed.length > 0 && (
            <>
              Не закрыты: <span className="font-mono">{notClosed.join(', ')}</span> в состоянии
              «Ошибка».{' '}
            </>
          )}
          После снятия запрета убыток дня считается заново.
        </p>
      </div>
      {children}
    </section>
  );
}

/** The link to the section «Риск» in the look of a button. */
export function RiskLink({ className, children }: { className: string; children: ReactNode }) {
  return (
    <Link to="/risk" className={className}>
      {children}
    </Link>
  );
}
