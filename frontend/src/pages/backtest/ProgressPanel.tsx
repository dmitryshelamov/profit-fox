import { type Progress, ProgressBar, progressPercent } from '@/modules/backtests';
import { groupDigits, showDecimal } from '@/shared/format/format';

const stages = ['Ожидание', 'Загрузка свечей', 'Расчёт', 'Готов'];
const current: Record<string, number> = { Pending: 0, LoadingCandles: 1, Running: 2 };

const dotOf = (index: number, now: number) =>
  index < now ? 'var(--color-ok)' : index === now ? 'var(--color-run)' : 'var(--color-faint)';

/** The stages of a backtest that is not finished, how far it is and how fast. */
export function ProgressPanel({
  status,
  progress,
  speed,
}: {
  status: string;
  progress: Progress;
  speed: string | null;
}) {
  const now = current[status] ?? 0;
  const percent = progressPercent(status, progress);

  return (
    <section
      aria-label="Ход расчёта"
      className="space-y-4 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <ol className="flex items-center gap-3 text-sm">
        {stages.map((title, index) => (
          <li
            key={title}
            aria-current={index === now ? 'step' : undefined}
            className={`flex items-center gap-2 ${index <= now ? 'text-text' : 'text-faint'}`}
          >
            <span className="size-2 rounded-full" style={{ background: dotOf(index, now) }} />
            {title}
          </li>
        ))}
      </ol>
      {percent !== undefined && <ProgressBar percent={percent} className="h-1.5 w-full" />}
      <div className="flex items-baseline justify-between gap-4 font-mono text-sm">
        <span className="flex gap-6">
          {status === 'Running' && (
            <span>
              {groupDigits(String(progress.calculatedCombinations))} из{' '}
              {groupDigits(String(progress.totalCombinations))} комбинаций
            </span>
          )}
          {status === 'Running' && speed !== null && (
            <span className="text-muted" title={speed}>
              {showDecimal(speed, 0)} свечей/с
            </span>
          )}
        </span>
        {status !== 'Pending' && (
          <span className="font-sans text-xs text-faint">
            Свечи: {groupDigits(String(progress.loadedCandles))} из{' '}
            {groupDigits(String(progress.expectedCandles))} загружены
          </span>
        )}
      </div>
    </section>
  );
}
