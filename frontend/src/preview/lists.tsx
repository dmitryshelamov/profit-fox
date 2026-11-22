import { type ReactNode, useEffect, useRef } from 'react';

/** The head of a list of the preview: column names of 11 px that stay in place while the list scrolls. */
export function ListHead({
  columns,
}: {
  readonly columns: readonly (readonly [string, string?])[];
}) {
  return (
    <thead className="sticky top-0 z-10 bg-panel text-left">
      <tr>
        {columns.map(([title, className]) => (
          <th
            key={title}
            className={`h-9 border-b border-line px-4 text-[11px] font-semibold whitespace-nowrap text-muted uppercase ${className ?? ''}`}
          >
            {title}
          </th>
        ))}
      </tr>
    </thead>
  );
}

/**
 * A row of a list of the preview. The selected row is marked by `aria-selected` and by a bar of the accent color, and
 * comes into view when it becomes selected — by a click on its mark on the chart, when it may be far down the list —
 * and again with every further click on a mark (`markClicks`), after the list was scrolled away from it.
 */
export function ListRow({
  label,
  selected,
  markClicks,
  dim = false,
  children,
}: {
  readonly label: string;
  readonly selected: boolean;
  /** How many times a mark of the chart was clicked. */
  readonly markClicks: number;
  readonly dim?: boolean;
  readonly children: ReactNode;
}) {
  const row = useRef<HTMLTableRowElement>(null);
  useEffect(() => {
    if (selected) {
      row.current?.scrollIntoView?.({ block: 'nearest' });
    }
  }, [selected, markClicks]);

  return (
    <tr
      ref={row}
      aria-label={label}
      aria-selected={selected}
      className={`h-10 border-b border-line ${
        selected ? 'bg-panel-2 shadow-[inset_2px_0_0_var(--color-accent)]' : 'hover:bg-panel-2'
      } ${dim ? 'text-muted' : ''}`}
    >
      {children}
    </tr>
  );
}

/** The moment of a row as a button that shows it on the chart. */
export function MomentButton({
  moment,
  onClick,
}: {
  readonly moment: string;
  readonly onClick: () => void;
}) {
  return (
    <button type="button" className="cursor-pointer font-mono hover:text-accent" onClick={onClick}>
      {moment}
    </button>
  );
}
