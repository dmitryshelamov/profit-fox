import type { PreviewCandle } from '@/api/client';

import { ListHead, ListRow, MomentButton } from './lists';
import { closeAt, type EventMark, formatUtc } from './preview';

/**
 * The events of the preview in time order: the moment in UTC and the close of the candle it happened on. A moment is a
 * button that shows its event on the chart; the row of the selected event is marked (spec signal-constructor «Метка
 * события выбирает строку»).
 */
export function EventList({
  marks,
  candles,
  timeframe,
  selected,
  onPick,
}: {
  readonly marks: readonly EventMark[];
  readonly candles: readonly PreviewCandle[];
  readonly timeframe: string;
  /** The moment of the selected event. */
  readonly selected: number | null;
  readonly onPick: (mark: EventMark) => void;
}) {
  if (marks.length === 0) {
    return <p className="p-4 text-muted">Событий нет.</p>;
  }

  return (
    <table aria-label="События" className="w-full border-collapse text-[13px]">
      <ListHead columns={[['Момент, UTC'], ['Закрытие свечи', 'text-right']]} />
      <tbody>
        {marks.map((mark) => (
          <ListRow
            key={mark.moment}
            label={`Событие ${formatUtc(mark.moment)}`}
            selected={mark.moment === selected}
          >
            <td className="px-4">
              <MomentButton moment={formatUtc(mark.moment)} onClick={() => onPick(mark)} />
            </td>
            <td className="px-4 text-right font-mono">
              {closeAt(candles, mark.moment, timeframe) ?? '—'}
            </td>
          </ListRow>
        ))}
      </tbody>
    </table>
  );
}
