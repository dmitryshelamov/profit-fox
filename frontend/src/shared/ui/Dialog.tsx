import { type ReactNode, useEffect, useRef } from 'react';

/** A modal dialog over a dimmed screen: the focus moves into it, and Escape closes it when it can be closed. */
export function Dialog({
  title,
  children,
  actions,
  width = 520,
  onClose,
}: {
  title: string;
  children?: ReactNode;
  actions: ReactNode;
  width?: number;
  /** Closes the dialog without its action, on Escape. */
  onClose?: () => void;
}) {
  const dialog = useRef<HTMLDivElement>(null);

  useEffect(() => {
    dialog.current?.focus();
  }, []);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#05060a]/60 p-4">
      <div
        ref={dialog}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            onClose?.();
          }
        }}
        className="space-y-4 rounded-xl border border-line bg-panel p-6 shadow-2xl outline-none"
        style={{ width }}
      >
        <h2 className="text-[17px] font-bold">{title}</h2>
        {children}
        <div className="flex justify-end gap-2.5">{actions}</div>
      </div>
    </div>
  );
}
