import { Badge } from '@/shared/ui/Badge';

import { type Progress, progressText, statusTitle } from '../model/status';

const dots: Record<string, string> = {
  Succeeded: 'var(--color-ok)',
  Failed: 'var(--color-err)',
  Running: 'var(--color-run)',
  LoadingCandles: 'var(--color-run)',
};

/** The status of a backtest and, while it is loading or calculating, how far it is. */
export function StatusBadge({ status, progress }: { status: string; progress: Progress }) {
  const text = progressText(status, progress);
  const title = statusTitle(status);

  return (
    <Badge dot={dots[status] ?? 'var(--color-faint)'}>{text ? `${title} · ${text}` : title}</Badge>
  );
}
