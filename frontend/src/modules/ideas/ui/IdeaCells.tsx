import { setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import type { Schemas } from '@/shared/api/client';

import { sideLabel, statusLabel } from '../model/labels';
import { probabilityView } from '../model/stats';

export type Idea = Schemas['IdeaResponse'];

const badgeColour = { '▲': 'text-ok', '▼': 'text-err', '≈': 'text-muted' } as const;

/** The probability of an idea's bucket with N and the badge against the baseline, or «мало данных». */
export function ProbabilityCell({ idea }: { idea: Idea }) {
  const view = probabilityView(idea.bucket, idea.baseline);

  if (!view.enough) {
    return <span className="text-muted">мало данных</span>;
  }

  return (
    <span className="whitespace-nowrap">
      {view.percent} %<span className="ml-1 text-xs text-faint">N {view.cases}</span>
      <span
        role="img"
        aria-label={
          view.badge === '▲'
            ? 'выше базовой линии'
            : view.badge === '▼'
              ? 'ниже базовой линии'
              : 'не отличается от базовой линии'
        }
        className={`ml-1 ${badgeColour[view.badge!]}`}
      >
        {view.badge}
      </span>
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

/** A setup of an idea as a small badge with its interval. */
export function SetupBadge({ setup, interval }: { setup: string; interval?: string }) {
  return (
    <span className="rounded bg-panel-2 px-1.5 py-0.5 text-xs whitespace-nowrap">
      {setupLabel(setup)}
      {interval !== undefined && <span className="ml-1 text-faint">{intervalLabel(interval)}</span>}
    </span>
  );
}
