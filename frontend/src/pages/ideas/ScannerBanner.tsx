import type { ScannerState } from '@/api/ideas';
import { momentText } from '@/pages/market/format';

/** Nobody kept the live data for longer than this, in seconds: new ideas do not come (spec «Сканер не работает»). */
export const idleWarningSeconds = 60;

/**
 * The state of the live data above the ideas (spec strategy-ideas «Состояние сканера в разделе «Идеи»»): the warm-up
 * while it goes, a warning when nobody keeps the live data longer than a minute, the moment ideas count without a break.
 */
export function ScannerBanner({ state }: { readonly state: ScannerState }) {
  const idle = state.live ? 0 : Number(state.idleSeconds ?? Number.POSITIVE_INFINITY);
  if (!state.live && idle > idleWarningSeconds) {
    return (
      <p
        role="status"
        aria-label="Живые данные"
        className="rounded-md border border-warn/40 bg-warn-bg px-4 py-2.5 text-warn"
      >
        Живые данные никто не ведёт{state.worker ? ` (последним был ${state.worker})` : ''}: новые
        идеи не появляются.
      </p>
    );
  }

  const pairs = Number(state.pairs);
  const warmed = Number(state.warmed);
  if (pairs > 0 && warmed < pairs) {
    return (
      <p
        role="status"
        aria-label="Живые данные"
        className="rounded-md border border-line bg-panel px-4 py-2.5 text-muted"
      >
        <span
          className="mr-2 inline-block size-1.5 rounded-full bg-run align-middle"
          aria-hidden="true"
        />
        идёт прогрев: {warmed} из {pairs} пар
      </p>
    );
  }

  return (
    <p role="status" aria-label="Живые данные" className="text-[12px] text-muted">
      {state.liveSince != null
        ? `идеи считаются без перерыва с ${momentText(state.liveSince)} UTC`
        : 'живые данные ведутся'}
    </p>
  );
}
