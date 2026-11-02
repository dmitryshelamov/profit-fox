/** A thin bar of how far a backtest is. */
export function ProgressBar({
  percent,
  className = 'w-24',
}: {
  percent: number;
  className?: string;
}) {
  return (
    <span
      role="progressbar"
      aria-label="Ход бэктеста"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={percent}
      className={`block h-1 overflow-hidden rounded-full bg-[#2d323d] ${className}`}
    >
      <span className="block h-full bg-run" style={{ width: `${percent}%` }} />
    </span>
  );
}
