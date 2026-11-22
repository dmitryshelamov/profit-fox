import { type KeyboardEvent, useEffect, useRef, useState } from 'react';

export interface MenuItem {
  readonly label: string;
  /** A dangerous action is told from the others by its color and stands after a divider. */
  readonly danger?: boolean;
  readonly onSelect: () => void;
}

/**
 * The menu of a row: the button «⋯» and the list of its actions. Escape and a click outside close it without an
 * action; the arrows move between the items. The focus returns to the button unless the click outside gave it to
 * something else — a field, or the menu of another row.
 */
export function Menu({
  label,
  items,
}: {
  readonly label: string;
  readonly items: readonly MenuItem[];
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLSpanElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    root.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
    const away = (event: MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) {
        setOpen(false);
        // After the click has moved the focus to what was clicked: it stays there, and only a focus left nowhere
        // by the closed menu returns to the button.
        setTimeout(() => {
          if (document.activeElement === null || document.activeElement === document.body) {
            button.current?.focus();
          }
        });
      }
    };
    document.addEventListener('click', away);
    return () => document.removeEventListener('click', away);
  }, [open]);

  const close = () => {
    setOpen(false);
    button.current?.focus();
  };

  const press = (event: KeyboardEvent) => {
    if (!open) {
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close();
      return;
    }

    const step = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
    if (step === 0) {
      return;
    }

    event.preventDefault();
    const all = [...(root.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])];
    const at = all.indexOf(document.activeElement as HTMLButtonElement);
    all[(at + step + all.length) % all.length]?.focus();
  };

  return (
    <span ref={root} className="relative inline-flex" onKeyDown={press}>
      <button
        ref={button}
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        className="grid size-8 cursor-pointer place-items-center rounded-md text-base leading-none text-muted hover:bg-panel-2 hover:text-text aria-expanded:bg-panel-2 aria-expanded:text-text"
        onClick={() => setOpen((current) => !current)}
      >
        ⋯
      </button>
      {open && (
        <div
          role="menu"
          aria-label={label}
          className="absolute top-full right-0 z-30 mt-1 flex w-48 flex-col rounded-lg border border-line bg-panel-2 py-1.5 shadow-2xl"
        >
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              tabIndex={-1}
              className={`cursor-pointer px-3.5 py-2 text-left text-[13px] outline-none hover:bg-panel focus:bg-panel ${
                item.danger ? 'mt-1.5 border-t border-line pt-3 text-err' : 'text-text'
              }`}
              onClick={() => {
                close();
                item.onSelect();
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </span>
  );
}
