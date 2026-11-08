import { useQuery } from '@tanstack/react-query';

import { formatClock, roundDecimal } from '@/shared/format/format';

import { guardsQuery } from '../api';

/**
 * Shown while the daily stop holds: the autos with money do not enter until the next UTC day, told in the local time;
 * manual entries and open trades work as usual.
 */
export function DailyStopBanner() {
  const guards = useQuery(guardsQuery()).data;

  if (guards === undefined || guards.triggeredAt === null || guards.resumesAt === null) {
    return null;
  }

  return (
    <section
      role="status"
      aria-label="Дневной стоп"
      className="space-y-1 rounded-xl border border-err/40 bg-err-bg px-4 py-3 text-err"
    >
      <p className="font-bold">
        Дневной стоп сработал — автоматы не входят до {formatClock(guards.resumesAt)}
      </p>
      <p className="text-xs">
        Сегодня {roundDecimal(guards.daySum, 2)} из {guards.dailyLossLimit} USDT · сработал в{' '}
        {formatClock(guards.triggeredAt)} · ручные входы и открытые сделки работают как обычно
      </p>
    </section>
  );
}
