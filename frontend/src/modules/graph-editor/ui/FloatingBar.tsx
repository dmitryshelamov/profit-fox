import type { HTMLAttributes } from 'react';

/** A bar that floats over the canvas, 44 px high: the document and its state on the left, the actions on the right. */
export function FloatingBar({ className = '', ...rest }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={`pointer-events-auto flex h-11 items-center gap-3 rounded-[10px] border border-line bg-panel/95 px-3 text-[13px] shadow-xl ${className}`}
      {...rest}
    />
  );
}

/** The divider between the parts of a bar. */
export function BarDivider() {
  return <span aria-hidden="true" className="h-5 w-px shrink-0 bg-line" />;
}
