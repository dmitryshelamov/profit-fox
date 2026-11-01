import type { ReactNode } from 'react';

const tones = {
  neutral: 'bg-[#2d323d] text-text',
  warn: 'bg-warn-bg text-warn',
  builtIn: 'border border-[#6b5326] bg-[#3a2c12] text-[#f3e3c4]',
} as const;

export function Badge({
  tone = 'neutral',
  dot,
  children,
}: {
  tone?: keyof typeof tones;
  dot?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={`inline-flex h-5 items-center gap-1.5 rounded-full px-2 text-[11px] font-semibold ${tones[tone]}`}
    >
      {dot && <span className="size-[7px] rounded-full" style={{ background: dot }} />}
      {children}
    </span>
  );
}
