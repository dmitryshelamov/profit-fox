import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { createPortal } from 'react-dom';

import { listScoringLabels, scoreText, scoreTone, scoringLabelsKey } from '@/api/scoring';
import { Badge } from '@/shared/ui/Badge';

import { labelHint } from './labelHint';

/** How many labels of an idea a row shows before «+N» (spec idea-labels «Метки в списках идей»). */
export const shownLabels = 2;

/** Where the hint stands: under the badges, or above them when the screen ends below. */
interface Place {
  readonly left: number;
  readonly top?: number;
  readonly bottom?: number;
}

/**
 * The labels of an idea: `most` badges (two by default) and how many more, a dash when it has none. Pointing at them
 * or focusing them shows the hint over the page (change label-score-hint, spec idea-labels «Подсказка меток с весами»):
 * the score of the idea's side, the labels with a weight by what they add, the rest in one line. Before the weights are
 * read, or for an idea without a side, the hint is the plain list.
 */
export function LabelBadges({
  labels,
  most = shownLabels,
  side,
}: {
  readonly labels: readonly string[];
  readonly most?: number;
  readonly side?: string;
}) {
  const [place, setPlace] = useState<Place | null>(null);
  const id = useId();
  const weights = useQuery({
    queryKey: scoringLabelsKey,
    queryFn: listScoringLabels,
    staleTime: 60_000,
    enabled: labels.length > 0 && side !== undefined,
  });
  if (labels.length === 0) {
    return <span className="text-faint">—</span>;
  }

  const show = (target: HTMLElement) => {
    const box = target.getBoundingClientRect();
    const room = window.innerHeight - box.bottom;
    setPlace(
      room < 260 && box.top > room
        ? { left: box.left, bottom: window.innerHeight - box.top + 6 }
        : { left: box.left, top: box.bottom + 6 },
    );
  };

  return (
    <span
      className="flex items-center gap-1.5"
      tabIndex={0}
      aria-describedby={place ? id : undefined}
      onMouseEnter={(event) => show(event.currentTarget)}
      onMouseLeave={() => setPlace(null)}
      onFocus={(event) => show(event.currentTarget)}
      onBlur={() => setPlace(null)}
    >
      {labels.slice(0, most).map((label) => (
        <Badge key={label} tone="muted">
          {label}
        </Badge>
      ))}
      {labels.length > most && <Badge tone="muted">+{labels.length - most}</Badge>}
      {place &&
        createPortal(
          <div
            id={id}
            role="tooltip"
            className="pointer-events-none fixed z-50 max-w-80 rounded-md border border-line bg-panel-2 px-3 py-2 text-[12px] shadow-lg"
            style={{ left: place.left, top: place.top, bottom: place.bottom }}
          >
            {side !== undefined && weights.data ? (
              <Weighted labels={labels} side={side} weights={weights.data} />
            ) : (
              <ul>
                {labels.map((label) => (
                  <li key={label}>{label}</li>
                ))}
              </ul>
            )}
          </div>,
          document.body,
        )}
    </span>
  );
}

function Weighted({
  labels,
  side,
  weights,
}: {
  readonly labels: readonly string[];
  readonly side: string;
  readonly weights: Parameters<typeof labelHint>[2];
}) {
  const hint = labelHint(labels, side, weights);
  return (
    <div className="flex flex-col gap-1">
      <div className="flex justify-between gap-6 font-semibold">
        <span>{side === 'long' ? 'Балл лонга' : 'Балл шорта'}</span>
        <span className={`font-mono ${scoreTone(hint.score)}`}>{scoreText(hint.score)}</span>
      </div>
      {hint.weighted.length > 0 && (
        <ul className="border-t border-line pt-1">
          {hint.weighted.map((label) => (
            <li key={label.text} className="flex justify-between gap-6">
              <span>{label.text}</span>
              <span className={`font-mono ${scoreTone(label.weight)}`}>
                {scoreText(label.weight)}
              </span>
            </li>
          ))}
        </ul>
      )}
      {hint.unweighted.length > 0 && (
        <p className="border-t border-line pt-1 text-muted">
          без веса ({hint.unweighted.length}): {hint.unweighted.join(', ')}
        </p>
      )}
    </div>
  );
}
