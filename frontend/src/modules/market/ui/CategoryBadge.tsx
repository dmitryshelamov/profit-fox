/** The market category of a pair as a badge: linear in the colour of contracts, spot grey. */
export function CategoryBadge({ category }: { category: string }) {
  return (
    <span
      className={`inline-block rounded px-1.5 py-px font-mono text-[11px] ${
        category === 'linear' ? 'bg-run/15 text-run' : 'bg-panel-2 text-muted'
      }`}
    >
      {category}
    </span>
  );
}
