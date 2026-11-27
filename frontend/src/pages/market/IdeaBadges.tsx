import type { IdeaBadge } from '@/api/market';
import { Badge } from '@/shared/ui/Badge';

/** The open ideas of a pair in its row: the first with its strategy, timeframe and side, then how many more. */
export function IdeaBadges({ ideas }: { readonly ideas: readonly IdeaBadge[] }) {
  const [first, ...rest] = ideas;
  if (first === undefined) {
    return <span className="text-faint">—</span>;
  }

  return (
    <span className="flex items-center gap-1.5">
      <Badge mono tone={first.side === 'short' ? 'err' : 'ok'} title={ideas.map(words).join(', ')}>
        {first.side === 'short' ? '▼' : '▲'} {first.strategy} {first.timeframe}
      </Badge>
      {rest.length > 0 && (
        <Badge mono title={rest.map(words).join(', ')}>
          +{rest.length}
        </Badge>
      )}
    </span>
  );
}

function words(idea: IdeaBadge): string {
  return `${idea.strategy} ${idea.timeframe} ${idea.side === 'short' ? 'шорт' : 'лонг'}`;
}
