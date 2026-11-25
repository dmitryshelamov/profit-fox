import type { ScannerState } from '@/api/ideas';
import { momentText } from '@/pages/market/format';

/** Nobody kept the live data for longer than this, in seconds: new ideas do not come (spec «Сканер не работает»). */
export const idleWarningSeconds = 60;

/**
 * How long the warm-up has still to go, in whole minutes rounded up: «осталось ≈ 9 мин», «осталось ≈ 1 ч 05 мин», or
 * «осталось меньше минуты».
 */
export function timeLeft(seconds: number): string {
  if (seconds < 60) {
    return 'осталось меньше минуты';
  }

  const minutes = Math.ceil(seconds / 60);
  if (minutes < 60) {
    return `осталось ≈ ${minutes} мин`;
  }

  return `осталось ≈ ${Math.floor(minutes / 60)} ч ${String(minutes % 60).padStart(2, '0')} мин`;
}

/**
 * The state of the live data above the ideas (spec strategy-ideas «Состояние сканера в разделе «Идеи»»): the warm-up
 * while it goes — or, when the reads of the warm-up fail, how many pairs fail and the last reason (task 18.3) —, a
 * warning when nobody keeps the live data longer than a minute, a warning while the feed catches up or is silent
 * (task 24.1), the moment ideas count without a break.
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
  const failed = Number(state.failed);
  if (failed > 0 && warmed < pairs) {
    return (
      <p
        role="status"
        aria-label="Живые данные"
        className="rounded-md border border-warn/40 bg-warn-bg px-4 py-2.5 text-warn"
      >
        прогрев не идёт у {failed} из {pairs} пар: {state.failure ?? 'сервис данных не ответил'}
      </p>
    );
  }

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
        {state.warmupLeftSeconds == null ? '' : ` · ${timeLeft(Number(state.warmupLeftSeconds))}`}
      </p>
    );
  }

  // The lease holds, but the feed catches up or has told no minute for longer than its limit (task 24.1).
  if (state.liveSince == null) {
    return (
      <p
        role="status"
        aria-label="Живые данные"
        className="rounded-md border border-warn/40 bg-warn-bg px-4 py-2.5 text-warn"
      >
        лента рынка догоняет или молчит: новые идеи появятся, когда она дойдёт до текущей минуты
      </p>
    );
  }

  return (
    <p role="status" aria-label="Живые данные" className="text-[12px] text-muted">
      идеи считаются без перерыва с {momentText(state.liveSince)} UTC
    </p>
  );
}
