import type { Schemas } from '@/shared/api/client';
import { formatClock, roundDecimal } from '@/shared/format/format';

type Holder = Schemas['PairHolderResponse'];

/** Whose trade holds the pair, and since when. */
function holderText(holder: Holder): string {
  const since = formatClock(holder.since);
  const account = holder.accountName ?? '';

  if (holder.kind === 'same_auto') {
    return holder.paper
      ? `учебная этого автомата, с ${since}`
      : `у этого автомата, открыта в ${since}`;
  }

  if (holder.kind === 'manual') {
    return `ручная на ${account}, с ${since}`;
  }

  if (holder.paper) {
    return `учебная «${holder.autoName}» на ${account}, с ${since}`;
  }

  return holder.above
    ? `на ${account} её взял «${holder.autoName}» — он выше в списке`
    : `на ${account} её уже взял «${holder.autoName}», с ${since}`;
}

const against = (word: string, detail: Schemas['RefusalDetailResponse']) =>
  `${word} ${detail.actual} при пределе ${detail.limit}`;

/** What exactly stopped an auto, by the kind of the first reason of its refusal; a dash when the reason has nothing to add. */
export function refusalDetailText(refusal: {
  readonly reasons: readonly string[];
  readonly detail: Schemas['RefusalDetailResponse'] | null;
}): string {
  const { detail } = refusal;

  if (detail === null) {
    return '—';
  }

  switch (refusal.reasons[0]) {
    case 'risk_reward_too_low':
      return `${roundDecimal(detail.actual!, 2)} < ${detail.limit}`;
    case 'not_enough_money':
      return `свободно ${roundDecimal(detail.actual!, 2)} из ${detail.limit} USDT`;
    case 'daily_stop':
      return against('сумма суток', detail);
    case 'too_many_open_trades':
    case 'too_many_same_direction':
      return against('открыто', detail);
    case 'total_risk_exceeded':
      return against('убыток по стопам', detail);
    case 'budget_taken':
    case 'autos_budget_taken':
      return `занято ${roundDecimal(detail.actual!, 2)} из ${detail.limit} USDT`;
    case 'amount_above_max':
      return against('сумма', detail);
    case 'pair_taken':
      return holderText(detail.holder!);
    case 'minute_limit':
    case 'side_limit':
      return `пропущено ${detail.actual} при лимите ${detail.limit}`;
    default:
      return '—';
  }
}
