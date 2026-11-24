/** A switch on or off, 36 by 20 px: lit with the accent when on. */
export function Switch({
  label,
  checked,
  disabled = false,
  onChange,
}: {
  readonly label: string;
  readonly checked: boolean;
  readonly disabled?: boolean;
  readonly onChange: (checked: boolean) => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-label={label}
      aria-checked={checked}
      disabled={disabled}
      className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-45 ${checked ? 'bg-accent' : 'bg-chip'}`}
      onClick={() => onChange(!checked)}
    >
      <span
        aria-hidden="true"
        className={`absolute size-3.5 rounded-full transition-[left] ${checked ? 'left-[19px] bg-on-accent' : 'left-[3px] bg-muted'}`}
      />
    </button>
  );
}
