import { useState } from 'react';

import { formatMoment } from '@/shared/format/format';

import type { Idea } from './IdeaCells';
import { cell, firedCell, firedColumn, ideaColumns, IdeaRowCells } from './IdeaRow';

const headers = [firedColumn, ...ideaColumns];

function Row({
  idea,
  amount,
  selected,
  onSelect,
  onEnter,
  className = '',
}: {
  idea: Idea;
  amount: string;
  selected: boolean;
  onSelect: (idea: Idea) => void;
  onEnter?: (idea: Idea) => void;
  className?: string;
}) {
  return (
    <tr
      onClick={() => onSelect(idea)}
      className={`cursor-pointer bg-panel hover:bg-panel-2 has-aria-pressed:bg-panel-2 has-aria-pressed:outline has-aria-pressed:outline-accent ${className}`}
    >
      <td className={firedCell}>
        {/* The click of the button goes up to the row. */}
        <button
          type="button"
          aria-pressed={selected}
          className="hover:text-text aria-pressed:text-text"
        >
          {formatMoment(idea.firedAt)}
        </button>
      </td>
      <IdeaRowCells idea={idea} amount={amount} />
      {onEnter && (
        <td className={`${cell} text-right`}>
          {(idea.status === 'awaiting_entry' || idea.status === 'active') && (
            <button
              type="button"
              onClick={(event) => {
                // The row below selects its idea on a click; entering is another act.
                event.stopPropagation();
                onEnter(idea);
              }}
              className="h-7 rounded-md border border-line px-3 text-xs font-semibold hover:bg-panel-2"
            >
              Войти
            </button>
          )}
        </td>
      )}
    </tr>
  );
}

/**
 * The ideas of a pair as a table with the columns of the ideas but the instrument, in the order given: its headers do not
 * sort. Clicking a row selects its idea; the found time is a button for the keyboard, pressed on the selected idea. The
 * expired ideas come under a row that opens and closes them; it starts closed whatever idea is selected.
 */
export function PairIdeasTable({
  open,
  expired,
  amount,
  selectedId,
  onSelect,
  onEnter,
}: {
  open: readonly Idea[];
  expired: readonly Idea[];
  amount: string;
  selectedId: string | undefined;
  onSelect: (idea: Idea) => void;
  /** Adds a column with «Войти» in the rows of the ideas that can be entered. */
  onEnter?: (idea: Idea) => void;
}) {
  const [expiredShown, setExpiredShown] = useState(false);

  return (
    <table className="w-full border-separate border-spacing-y-1 text-sm">
      <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
        <tr>
          {headers.map(({ column, label, right }) => (
            <th key={column} scope="col" className={`${cell} ${right ? 'text-right' : ''}`}>
              {label}
            </th>
          ))}
          {onEnter && (
            <th scope="col" className={cell}>
              <span className="sr-only">Вход в сделку</span>
            </th>
          )}
        </tr>
      </thead>
      <tbody>
        {open.map((idea) => (
          <Row
            key={idea.id}
            idea={idea}
            amount={amount}
            selected={idea.id === selectedId}
            onSelect={onSelect}
            onEnter={onEnter}
          />
        ))}
      </tbody>
      {expired.length > 0 && (
        <tbody>
          <tr>
            <th
              colSpan={headers.length + (onEnter ? 1 : 0)}
              scope="rowgroup"
              className={`${cell} text-left text-xs font-normal tracking-wide text-faint uppercase`}
            >
              <button
                type="button"
                aria-expanded={expiredShown}
                onClick={() => setExpiredShown(!expiredShown)}
                className="tracking-wide uppercase hover:text-text"
              >
                Истёкшие · {expired.length}
              </button>
            </th>
          </tr>
          {expiredShown &&
            expired.map((idea) => (
              <Row
                key={idea.id}
                idea={idea}
                amount={amount}
                selected={idea.id === selectedId}
                onSelect={onSelect}
                onEnter={onEnter}
                className="opacity-70"
              />
            ))}
        </tbody>
      )}
    </table>
  );
}
