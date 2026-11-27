import type { ReactNode } from 'react';

/** The tones of a badge: what it says goes by the word, the tone only colors it. */
export type BadgeTone = 'neutral' | 'muted' | 'ok' | 'warn' | 'err' | 'run' | 'accent';

const tones: Record<BadgeTone, string> = {
  neutral: 'bg-chip text-text',
  muted: 'bg-chip text-muted',
  ok: 'bg-ok-bg text-ok',
  warn: 'bg-warn-bg text-warn',
  err: 'bg-err-bg text-err',
  run: 'bg-run-bg text-run',
  accent: 'bg-accent/15 text-accent',
};

/** A word on a colored ground, 20 px high, with a dot on the left when asked. The tone is told by `data-tone`. */
export function Badge({
  tone = 'neutral',
  dot = false,
  mono = false,
  title,
  className = '',
  children,
}: {
  readonly tone?: BadgeTone;
  readonly dot?: boolean;
  readonly mono?: boolean;
  readonly title?: string;
  readonly className?: string;
  readonly children: ReactNode;
}) {
  return (
    <span
      data-tone={tone}
      title={title}
      className={`inline-flex h-5 shrink-0 items-center gap-1.5 rounded-[10px] px-2 text-[11px] leading-none font-medium whitespace-nowrap ${mono ? 'font-mono' : ''} ${tones[tone]} ${className}`}
    >
      {dot && <span aria-hidden="true" className="size-1.5 rounded-full bg-current" />}
      {children}
    </span>
  );
}
