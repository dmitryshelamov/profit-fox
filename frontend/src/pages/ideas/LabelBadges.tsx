import { Badge } from '@/shared/ui/Badge';

/** How many labels of an idea a row shows before «+N» (spec idea-labels «Метки в списках идей»). */
export const shownLabels = 2;

/**
 * The labels of an idea: `most` badges (two by default) and how many more, the whole list in the hint; a dash when it has
 * none.
 */
export function LabelBadges({
  labels,
  most = shownLabels,
}: {
  readonly labels: readonly string[];
  readonly most?: number;
}) {
  if (labels.length === 0) {
    return <span className="text-faint">—</span>;
  }

  return (
    <span className="flex items-center gap-1.5" title={labels.join(', ')}>
      {labels.slice(0, most).map((label) => (
        <Badge key={label} tone="muted">
          {label}
        </Badge>
      ))}
      {labels.length > most && <Badge tone="muted">+{labels.length - most}</Badge>}
    </span>
  );
}
