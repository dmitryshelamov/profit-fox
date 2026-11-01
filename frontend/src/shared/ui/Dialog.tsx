import type { ReactNode } from 'react';

/** A modal dialog over a dimmed screen. */
export function Dialog({
  title,
  children,
  actions,
  width = 520,
}: {
  title: string;
  children?: ReactNode;
  actions: ReactNode;
  width?: number;
}) {
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#05060a]/60 p-4">
      <div
        role="dialog"
        aria-label={title}
        className="space-y-4 rounded-xl border border-line bg-panel p-6 shadow-2xl"
        style={{ width }}
      >
        <h2 className="text-[17px] font-bold">{title}</h2>
        {children}
        <div className="flex justify-end gap-2.5">{actions}</div>
      </div>
    </div>
  );
}
