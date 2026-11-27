import { stateLabel } from '@/pages/runs/labels';

import { Badge, type BadgeTone } from './Badge';

const tones: Record<string, BadgeTone> = {
  queued: 'neutral',
  running: 'run',
  done: 'ok',
  failed: 'err',
  cancelled: 'neutral',
};

/**
 * The state of a run as a word on a colored ground, the same on every page (spec bench-ui «Цвет состояния прогона»):
 * «Готов» green, «Упал» red, «Идёт» blue, «Отменён» and «В очереди» neutral.
 */
export function RunStatusBadge({ state }: { readonly state: string }) {
  return (
    <Badge tone={tones[state] ?? 'neutral'} dot>
      {stateLabel(state)}
    </Badge>
  );
}
