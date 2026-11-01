import type { ReactNode } from 'react';

/** A floating card on the right of the canvas: node properties, parameters, errors. */
export function Card({
  title,
  label,
  onClose,
  tone = 'line',
  children,
}: {
  title: ReactNode;
  label: string;
  onClose: () => void;
  tone?: 'line' | 'err';
  children: ReactNode;
}) {
  return (
    <section
      aria-label={label}
      className={`absolute top-[76px] right-4 z-10 max-h-[calc(100vh-160px)] w-[300px] overflow-y-auto rounded-[10px] border bg-panel p-4 shadow-2xl ${tone === 'err' ? 'border-[#5a2a2e]' : 'border-line'}`}
    >
      <header className="mb-3 flex items-start justify-between gap-2">
        <div className="text-sm font-bold">{title}</div>
        <button
          type="button"
          aria-label="Закрыть"
          onClick={onClose}
          className="text-muted hover:text-text"
        >
          ×
        </button>
      </header>
      <div className="space-y-4">{children}</div>
    </section>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      <p className="text-[11px] font-semibold tracking-wide text-muted uppercase">{label}</p>
      {children}
    </div>
  );
}

/** Segmented choice of one value, as in the mockups. */
export function Segmented({
  label,
  options,
  value,
  disabled,
  onChange,
}: {
  label: string;
  options: readonly { value: string; title: string }[];
  value: string;
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="flex rounded-md border border-line bg-panel-2 p-[3px]"
    >
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={option.value === value}
          disabled={disabled}
          onClick={() => onChange(option.value)}
          className={`h-[26px] flex-1 rounded text-sm ${option.value === value ? 'border border-accent bg-[#343a47] font-semibold' : 'text-muted'}`}
        >
          {option.title}
        </button>
      ))}
    </div>
  );
}
