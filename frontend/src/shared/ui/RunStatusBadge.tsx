import type { Run } from '@/api/runs';
import { statusLabel } from '@/pages/runs/labels';

import { Badge, type BadgeTone } from './Badge';

const tones: Record<string, BadgeTone> = {
  passed: 'ok',
  insufficient_data: 'warn',
  failed: 'err',
  running: 'run',
  queued: 'muted',
};

/** What the badge of a run shows: the status of its line in the ledger, or where it is in the queue. */
export function statusOfRun(run: Run): string {
  return run.result?.status ?? run.state;
}

/**
 * The status of a run as a word on a colored ground, the same on every page (spec bench-ui «Цвет статуса прогона»):
 * a status of the ledger, or `running` and `queued` of the queue.
 */
export function RunStatusBadge({ status }: { readonly status: string }) {
  return (
    <Badge tone={tones[status] ?? 'neutral'} dot>
      {statusLabel(status)}
    </Badge>
  );
}
