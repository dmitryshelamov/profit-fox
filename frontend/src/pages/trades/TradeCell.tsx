import { useState } from 'react';

import type { Idea } from '@/api/ideas';
import type { RiskState } from '@/api/risk';
import { tradeStates } from '@/api/trades';
import { useRiskState } from '@/pages/risk/useRiskState';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

import { EntryDialog } from './EntryDialog';

/**
 * Why an entry is impossible by the state of the risk module alone (spec idea-trades «Кнопка „Войти“ у идеи»), or none:
 * no request to the exchange is made for it.
 */
export function entryBlock(
  state: RiskState | undefined,
  reading: 'pending' | 'error' | 'ready',
): string | null {
  if (state === undefined) {
    return reading === 'error' ? 'не удалось прочитать состояние риска' : 'читаем состояние риска';
  }

  if (state.halt.haltedAt !== null && state.halt.haltedAt !== undefined) {
    return 'вход запрещён стоп-краном';
  }

  if (!state.limitsSet) {
    return 'задайте пределы в разделе «Риск»';
  }

  if (Number(state.accounts) === 0) {
    return 'добавьте аккаунт в «Аккаунтах»';
  }

  return state.traderAlive ? null : 'нет торгового Worker’а: вход некому отправить';
}

/**
 * The column «Сделка» of the tables of ideas (spec idea-trades «Кнопка „Войти“ у идеи»): the state of the trade of the
 * idea, or «Войти» of an open idea — disabled with the reason in its hint — which opens the entry dialog. A click in it
 * is not a click on the row.
 */
export function TradeCell({ idea }: { readonly idea: Idea }) {
  const risk = useRiskState();
  const [entering, setEntering] = useState(false);
  const trade = idea.trade;
  if (trade) {
    return (
      <Badge tone={['pending', 'open', 'closing'].includes(trade.state) ? 'run' : 'muted'}>
        {['pending', 'open', 'closing'].includes(trade.state)
          ? 'в сделке'
          : (tradeStates[trade.state] ?? trade.state).toLowerCase()}
      </Badge>
    );
  }

  if (idea.state !== 'open') {
    return null;
  }

  const reason = entryBlock(risk.data, risk.isError ? 'error' : 'pending');
  return (
    <span className="font-sans" onClick={(event) => event.stopPropagation()}>
      <Button
        variant="primary"
        size="sm"
        aria-label={`Войти ${idea.symbol}`}
        title={reason ?? undefined}
        disabled={reason !== null}
        onClick={() => setEntering(true)}
      >
        Войти
      </Button>
      {entering && <EntryDialog idea={idea} onClose={() => setEntering(false)} />}
    </span>
  );
}
