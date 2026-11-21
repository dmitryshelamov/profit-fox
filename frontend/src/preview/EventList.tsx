import type { PreviewCandle } from '@/api/client';

import { closeAt, type EventMark, formatUtc } from './preview';

/**
 * The events of the preview in time order: the moment in UTC and the close of the candle it happened on. With
 * `onPick` a moment is a button that shows its event on the chart.
 */
export function EventList({
  marks,
  candles,
  timeframe,
  onPick,
}: {
  readonly marks: readonly EventMark[];
  readonly candles: readonly PreviewCandle[];
  readonly timeframe: string;
  readonly onPick?: (mark: EventMark) => void;
}) {
  if (marks.length === 0) {
    return <p className="text-sm text-zinc-400">Событий нет.</p>;
  }

  return (
    <table aria-label="События" className="w-full text-sm">
      <thead className="text-left text-zinc-400">
        <tr>
          <th className="py-1 font-normal">Момент, UTC</th>
          <th className="py-1 text-right font-normal">Закрытие свечи</th>
        </tr>
      </thead>
      <tbody className="font-mono">
        {marks.map((mark) => (
          <tr key={mark.moment} className="border-t border-zinc-800">
            <td className="py-1">
              {onPick ? (
                <button type="button" className="hover:text-amber-300" onClick={() => onPick(mark)}>
                  {formatUtc(mark.moment)}
                </button>
              ) : (
                formatUtc(mark.moment)
              )}
            </td>
            <td className="py-1 text-right">{closeAt(candles, mark.moment, timeframe) ?? '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
