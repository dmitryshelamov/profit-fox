import type { InputHTMLAttributes, ReactNode, Ref } from 'react';

/** A text field 32 px high, with a sign on the left when given. `className` sizes the field, the rest goes to the input. */
export function TextField({
  sign,
  mono = false,
  className = '',
  ref,
  ...input
}: InputHTMLAttributes<HTMLInputElement> & {
  readonly sign?: ReactNode;
  readonly mono?: boolean;
  readonly ref?: Ref<HTMLInputElement>;
}) {
  return (
    <span
      className={`flex h-8 items-center gap-2 rounded-md border border-line bg-panel-2 px-2.5 focus-within:border-accent ${className}`}
    >
      {sign !== undefined && (
        <span aria-hidden="true" className="text-faint">
          {sign}
        </span>
      )}
      <input
        ref={ref}
        className={`h-full min-w-0 flex-1 bg-transparent text-[13px] text-text outline-none placeholder:text-faint read-only:text-muted ${mono ? 'font-mono' : ''}`}
        {...input}
      />
    </span>
  );
}
