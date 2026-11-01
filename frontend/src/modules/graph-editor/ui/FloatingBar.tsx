import type { ReactNode } from 'react';

/** A floating bar over the canvas, as at the top corners of the mockups. */
export function FloatingBar({
  label,
  side,
  children,
}: {
  label: string;
  side: 'left' | 'right';
  children: ReactNode;
}) {
  return (
    <header
      aria-label={label}
      className={`absolute top-4 z-10 flex h-11 items-center gap-0.5 rounded-[10px] border border-line bg-panel px-1 shadow-xl ${side === 'left' ? 'left-4' : 'right-4'}`}
    >
      {children}
    </header>
  );
}

export function BarDivider() {
  return <span className="mx-1.5 h-5 w-px bg-line" />;
}
