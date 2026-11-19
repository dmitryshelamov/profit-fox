import type { ReactNode } from 'react';

/** A floating card over the right of the canvas: one at a time, closed by its cross. */
export function Card({
  title,
  onClose,
  children,
}: {
  readonly title: string;
  readonly onClose: () => void;
  readonly children: ReactNode;
}) {
  return (
    <section
      role="region"
      aria-label={title}
      className="pointer-events-auto flex max-h-full w-80 flex-col overflow-hidden rounded-lg border border-zinc-700 bg-zinc-900/95 shadow-2xl"
    >
      <header className="flex items-center justify-between border-b border-zinc-800 px-3 py-2">
        <h2 className="text-sm font-semibold">{title}</h2>
        <button
          type="button"
          aria-label="Закрыть"
          className="text-zinc-400 hover:text-zinc-100"
          onClick={onClose}
        >
          ✕
        </button>
      </header>
      <div className="flex flex-col gap-3 overflow-y-auto p-3 text-sm">{children}</div>
    </section>
  );
}

export const fieldClass =
  'rounded border border-zinc-700 bg-zinc-800 px-2 py-1 font-mono text-xs text-zinc-100';
export const buttonClass =
  'rounded border border-zinc-700 bg-zinc-800 px-2 py-1 text-xs text-zinc-100 hover:bg-zinc-700 disabled:opacity-40';
