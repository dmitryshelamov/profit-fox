import type { ReactNode } from 'react';

/**
 * A floating card over the right of the canvas, under the bar of actions: one at a time, 300 px wide, closed by its
 * cross. `title` is its accessible name; `heading` is what its header shows when that is more than the name.
 */
export function Card({
  title,
  heading,
  tone = 'plain',
  onClose,
  children,
}: {
  readonly title: string;
  readonly heading?: ReactNode;
  /** The card of errors has the border of an error. */
  readonly tone?: 'plain' | 'error';
  readonly onClose: () => void;
  readonly children: ReactNode;
}) {
  return (
    <section
      role="region"
      aria-label={title}
      className={`pointer-events-auto flex max-h-full w-[300px] shrink-0 flex-col overflow-hidden rounded-[10px] border bg-panel shadow-2xl ${
        tone === 'error' ? 'border-err-bg' : 'border-line'
      }`}
    >
      <header className="flex items-center justify-between gap-2 px-4 pt-3.5 pb-2">
        <h2 className="flex min-w-0 items-center gap-2 text-sm font-bold">{heading ?? title}</h2>
        <button
          type="button"
          aria-label="Закрыть"
          className="cursor-pointer text-muted hover:text-text"
          onClick={onClose}
        >
          ✕
        </button>
      </header>
      <div className="flex flex-col gap-3.5 overflow-y-auto px-4 pt-1 pb-4 text-[13px]">
        {children}
      </div>
    </section>
  );
}

/** The name of a field of a card: 11 px, semibold, capitals, with what stands next to it. */
export function FieldLabel({ children }: { readonly children: ReactNode }) {
  return (
    <span className="flex items-center gap-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
      {children}
    </span>
  );
}
