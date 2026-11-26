import type { PreviewCandle } from '@/api/client';
import { zoneLabel } from '@/shared/time';

import { ListHead, ListRow, MomentButton } from './lists';
import { closeAt, type EventMark, formatMoment } from './preview';

/**
 * The events of the preview in time order: the moment in the zone of the browser and the close of the candle it happened on. A moment is a
 * button that shows its event on the chart; the row of the selected event is marked (spec signal-constructor «Метка
 * события выбирает строку»).
 */
export function EventList({
  marks,
  candles,
  timeframe,
  selected,
  markClicks,
  onPick,
}: {
  readonly marks: readonly EventMark[];
  readonly candles: readonly PreviewCandle[];
  readonly timeframe: string;
  /** The moment of the selected event. */
  readonly selected: number | null;
  /** How many times a mark of the chart was clicked: each click brings the selected row into view. */
  readonly markClicks: number;
  readonly onPick: (mark: EventMark) => void;
}) {
  if (marks.length === 0) {
    return <p className="p-4 text-muted">Событий нет.</p>;
  }

  return (
    <table aria-label="События" className="w-full border-collapse text-[13px]">
      <ListHead columns={[[`Момент, ${zoneLabel()}`], ['Закрытие свечи', 'text-right']]} />
      <tbody>
        {marks.map((mark) => (
          <ListRow
            key={mark.moment}
            label={`Событие ${formatMoment(mark.moment)}`}
            selected={mark.moment === selected}
            markClicks={markClicks}
          >
            <td className="px-4">
              <MomentButton moment={formatMoment(mark.moment)} onClick={() => onPick(mark)} />
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
