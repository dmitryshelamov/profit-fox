import { type ReactNode, useEffect, useRef } from 'react';

/**
 * A modal dialog on `<dialog>` and `showModal()`: the page under it does not take the focus. Escape closes it through
 * `onClose` without an action; a dialog without `onClose` waits for one of its buttons.
 */
export function Dialog({
  label,
  title,
  className = 'w-[420px]',
  onClose,
  children,
}: {
  /** The accessible name of the dialog. */
  readonly label: string;
  readonly title?: string;
  readonly className?: string;
  readonly onClose?: () => void;
  readonly children: ReactNode;
}) {
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;
    if (element === null) {
      return;
    }

    if (!element.open) {
      element.showModal();
    }

    // The keys of a dialog with nothing to focus still reach it.
    if (!element.contains(document.activeElement)) {
      element.focus();
    }
  }, []);

  return (
    <dialog
      ref={dialog}
      aria-label={label}
      tabIndex={-1}
      className={`m-auto flex max-w-[calc(100vw-2rem)] flex-col gap-4 rounded-[10px] border border-line bg-panel p-5 text-[13px] text-text shadow-2xl outline-none backdrop:bg-app/70 ${className}`}
      onCancel={(event) => {
        event.preventDefault();
        onClose?.();
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          event.stopPropagation();
          onClose?.();
        }
      }}
    >
      {title !== undefined && <h2 className="text-base font-bold">{title}</h2>}
      {children}
    </dialog>
  );
}
