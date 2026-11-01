const colors: Record<string, string> = {
  number: 'var(--color-pin-number)',
  boolean: 'var(--color-pin-boolean)',
  duration: 'var(--color-pin-duration)',
};

export const pinTypeTitles: Record<string, string> = {
  number: 'число',
  boolean: 'логическое',
  duration: 'длительность',
};

export function pinColor(type: string): string {
  return colors[type] ?? 'var(--color-muted)';
}

/** A pin name with the color of its type. */
export function PinChip({ name, type }: { name: string; type: string }) {
  return (
    <span
      title={pinTypeTitles[type]}
      className="inline-flex h-5 items-center gap-1.5 rounded-full bg-[#2d323d] px-2 text-[11px] font-semibold"
    >
      <span className="size-[7px] rounded-full" style={{ background: pinColor(type) }} />
      {name}
    </span>
  );
}
