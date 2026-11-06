import type { Schemas } from '@/shared/api/client';

import {
  durationText,
  excludedText,
  ideaComparisonText,
  meanText,
  moneyText,
  outcomeCountsText,
  promiseComparisonText,
} from '../model/journal';

/** The totals of the closed trades of the filter: the outcomes, the money, the mean result with its interval, the time, and the two comparisons. */
export function JournalTotals({ totals }: { totals: Schemas['IdeaJournalTotalsResponse'] }) {
  const excluded = excludedText(totals);
  const rows: readonly (readonly [string, string])[] = [
    ['Исходы', outcomeCountsText(totals)],
    ['Результат', moneyText(totals)],
    ['Средний R', meanText(totals)],
    ['Время до исхода', durationText(totals.meanSecondsToOutcome)],
    ['Обещание', promiseComparisonText(totals.promise)],
    ['Идеи и сделки', ideaComparisonText(totals.ideas)],
  ];

  return (
    <section aria-label="Итоги" className="space-y-1 rounded-lg bg-panel p-4 text-sm">
      <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1">
        {rows.map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted">{label}</dt>
            <dd className="font-mono">{value}</dd>
          </div>
        ))}
      </dl>
      {excluded !== null && (
        <p className="text-xs text-faint">{excluded}: не входят в результат и R.</p>
      )}
    </section>
  );
}
