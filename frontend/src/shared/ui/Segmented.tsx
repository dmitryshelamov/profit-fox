import type { KeyboardEvent } from 'react';

export interface SegmentedOption<T extends string> {
  readonly value: T;
  readonly label: string;
}

/**
 * A choice of one of a few, all in sight: a radio group whose segments switch by a click and by the arrows. The group
 * is one stop of the Tab key, on the chosen segment.
 */
export function Segmented<T extends string>({
  label,
  value,
  options,
  disabled = false,
  mono = true,
  className = '',
  onChange,
}: {
  readonly label: string;
  readonly value: T | undefined;
  readonly options: readonly SegmentedOption<T>[];
  readonly disabled?: boolean;
  readonly mono?: boolean;
  readonly className?: string;
  readonly onChange: (value: T) => void;
}) {
  const chosen = options.findIndex((option) => option.value === value);

  const press = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const step =
      event.key === 'ArrowRight' || event.key === 'ArrowDown'
        ? 1
        : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
          ? -1
          : 0;
    if (step === 0 || disabled) {
      return;
    }

    event.preventDefault();
    const next = (index + step + options.length) % options.length;
    onChange(options[next]!.value);
    event.currentTarget.parentElement
      ?.querySelectorAll<HTMLButtonElement>('[role="radio"]')
      [next]?.focus();
  };

  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={`inline-flex h-8 items-stretch gap-0.5 rounded-md border border-line bg-panel-2 p-0.5 ${className}`}
    >
      {options.map((option, index) => {
        const checked = index === chosen;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={checked}
            tabIndex={checked || (chosen === -1 && index === 0) ? 0 : -1}
            disabled={disabled}
            className={`min-w-9 flex-1 cursor-pointer rounded px-2 text-xs disabled:cursor-not-allowed ${mono ? 'font-mono' : ''} ${
              checked
                ? 'border border-accent bg-panel font-semibold text-text'
                : 'border border-transparent text-muted enabled:hover:text-text'
            }`}
            onClick={() => onChange(option.value)}
            onKeyDown={(event) => press(event, index)}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
