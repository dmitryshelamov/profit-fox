import type { KeyboardEvent } from 'react';

export interface Tab<T extends string> {
  readonly value: T;
  readonly label: string;
  /** The number of lines under the tab, shown in its name. */
  readonly count?: number;
}

/** Tabs with a count in the name: they switch by a click and by the arrows; the panel is drawn by the page. */
export function Tabs<T extends string>({
  label,
  value,
  tabs,
  className = '',
  onChange,
}: {
  readonly label: string;
  readonly value: T;
  readonly tabs: readonly Tab<T>[];
  readonly className?: string;
  readonly onChange: (value: T) => void;
}) {
  const press = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }

    event.preventDefault();
    const next = (index + step + tabs.length) % tabs.length;
    onChange(tabs[next]!.value);
    event.currentTarget.parentElement
      ?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
      [next]?.focus();
  };

  return (
    <div role="tablist" aria-label={label} className={`flex items-end gap-5 ${className}`}>
      {tabs.map((tab, index) => {
        const selected = tab.value === value;
        return (
          <button
            key={tab.value}
            type="button"
            role="tab"
            aria-selected={selected}
            tabIndex={selected ? 0 : -1}
            className={`-mb-px flex h-10 cursor-pointer items-center gap-2 border-b-2 text-[13px] ${
              selected
                ? 'border-accent font-semibold text-text'
                : 'border-transparent text-muted hover:text-text'
            }`}
            onClick={() => onChange(tab.value)}
            onKeyDown={(event) => press(event, index)}
          >
            {tab.label}{' '}
            {tab.count !== undefined && (
              <span className="rounded-[10px] bg-chip px-1.5 py-0.5 font-mono text-[11px] leading-none font-normal text-muted">
                {tab.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
