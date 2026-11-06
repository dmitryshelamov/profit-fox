import type { Schemas } from '@/shared/api/client';
import { compareDecimals } from '@/shared/format/decimal';
import { showDecimal } from '@/shared/format/format';

import { sideLabel, statusLabel } from '../model/labels';
import { breakEvenText, expectancyText, outcomeShares, probabilityView } from '../model/stats';

export type Idea = Schemas['IdeaResponse'];

const badgeColour = { '▲': 'text-ok', '▼': 'text-err', '≈': 'text-muted' } as const;

const badgeLabel = {
  '▲': 'ожидание надёжно выше нуля',
  '▼': 'ожидание надёжно ниже нуля',
  '≈': 'ожидание не отличается от нуля',
} as const;

/**
 * The probability of an idea's bucket with the number of cases after it, «46 % · 412», or «мало данных»; under it, small, the break-even of
 * the idea, and on hover the shares of the outcomes of the bucket.
 */
export function ProbabilityCell({ idea }: { idea: Idea }) {
  const view = probabilityView(idea.bucket);

  return (
    <span className="inline-flex flex-col gap-0.5" title={outcomeShares(idea.bucket) ?? undefined}>
      {view.enough ? (
        <span className="whitespace-nowrap">
          {view.percent} %<span className="ml-1 text-xs text-faint">· {view.cases}</span>
        </span>
      ) : (
        <span className="text-muted">мало данных</span>
      )}
      <span className="whitespace-nowrap text-[11px] text-faint">
        {breakEvenText(idea.breakEven)}
      </span>
    </span>
  );
}

/** The expectancy of an idea's bucket in R, the badge of the bucket before it when the bucket has enough data. */
export function ExpectancyCell({ idea }: { idea: Idea }) {
  const { badge } = probabilityView(idea.bucket);

  return (
    <span className="whitespace-nowrap">
      {badge !== null && (
        <span role="img" aria-label={badgeLabel[badge]} className={`mr-1 ${badgeColour[badge]}`}>
          {badge}
        </span>
      )}
      {expectancyText(idea.bucket)}
    </span>
  );
}

/**
 * The reward to risk after the fees in a chip, green from 1, where the profit at the target equals the loss at the stop, red below;
 * under it, small, the ratio of the prices.
 */
export function RiskRewardCell({
  netRiskReward,
  riskReward,
}: {
  netRiskReward: string;
  riskReward: string;
}) {
  const enough = compareDecimals(netRiskReward, '1') >= 0;

  return (
    <span className="inline-flex flex-col items-end gap-0.5">
      <span
        className={`rounded px-1.5 py-0.5 ${enough ? 'bg-ok/15 text-ok' : 'bg-err-bg text-err'}`}
      >
        {showDecimal(netRiskReward, 2)}
      </span>
      <span className="text-[11px] text-faint">без ком. {showDecimal(riskReward, 2)}</span>
    </span>
  );
}

export function SideText({ side }: { side: string }) {
  return <span className={side === 'long' ? 'text-ok' : 'text-err'}>{sideLabel(side)}</span>;
}

const statusColour: Record<string, string> = {
  active: 'text-run',
  target: 'text-ok',
  stop: 'text-err',
  awaiting_entry: 'text-warn',
};

export function StatusText({ status }: { status: string }) {
  return <span className={statusColour[status] ?? 'text-muted'}>{statusLabel(status)}</span>;
}
