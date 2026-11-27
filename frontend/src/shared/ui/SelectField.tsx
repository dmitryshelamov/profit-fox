import type { SelectHTMLAttributes } from 'react';

/** A choice from a list, 32 px high. `className` sizes the field, the rest goes to the select. */
export function SelectField({
  mono = false,
  className = '',
  children,
  ...select
}: SelectHTMLAttributes<HTMLSelectElement> & { readonly mono?: boolean }) {
  return (
    <span className={`relative inline-flex h-8 ${className}`}>
      <select
        className={`h-full w-full cursor-pointer appearance-none rounded-md border border-line bg-panel-2 pr-7 pl-2.5 text-[13px] text-text outline-none focus:border-accent disabled:cursor-not-allowed disabled:opacity-45 ${mono ? 'font-mono' : ''}`}
        {...select}
      >
        {children}
      </select>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute top-1/2 right-2.5 -translate-y-1/2 text-[9px] text-faint"
      >
        ▼
      </span>
    </span>
  );
}
