import { intervalLabel } from '@/modules/market-data';
import { groupDigits } from '@/shared/format/format';

import { groupPairIdeas } from '../model/groups';
import { typeLabel } from '../model/labels';
import { expectancyText } from '../model/stats';
import { type Idea, ProbabilityCell, SetupBadge, SideText, StatusText } from './IdeaCells';

function IdeaItem({
  idea,
  selected,
  onSelect,
}: {
  idea: Idea;
  selected: boolean;
  onSelect: (idea: Idea) => void;
}) {
  return (
    <li>
      <button
        type="button"
        aria-pressed={selected}
        onClick={() => onSelect(idea)}
        className="w-full space-y-1 rounded-lg border border-line bg-panel px-3 py-2 text-left text-sm hover:bg-panel-2 aria-pressed:border-accent aria-pressed:bg-panel-2"
      >
        <span className="flex flex-wrap items-center gap-2">
          <SetupBadge setup={idea.setup} interval={idea.interval} />
          <span>{typeLabel(idea.type)}</span>
          <SideText side={idea.side} />
          <span className="ml-auto text-xs">
            <StatusText status={idea.status} />
          </span>
        </span>
        <span className="flex flex-wrap gap-x-3 font-mono text-xs text-muted">
          <span>Вход {groupDigits(idea.entry)}</span>
          <span>Стоп {groupDigits(idea.stop)}</span>
          <span>Цель {groupDigits(idea.target)}</span>
        </span>
        <span className="flex flex-wrap gap-x-3 text-xs text-muted">
          <ProbabilityCell idea={idea} />
          <span className="font-mono">{expectancyText(idea.bucket)}</span>
          <span className="text-faint">{intervalLabel(idea.interval)}</span>
        </span>
      </button>
    </li>
  );
}

/**
 * The ideas of a pair: those waiting for entry and the active ones first, in the order the API gave them (by expectancy),
 * then the group of the expired ones, closed in the last 24 hours. The selected idea is marked.
 */
export function PairIdeasList({
  ideas,
  selectedId,
  onSelect,
}: {
  ideas: readonly Idea[];
  selectedId: string | undefined;
  onSelect: (idea: Idea) => void;
}) {
  const { open, closed } = groupPairIdeas(ideas);

  if (ideas.length === 0) {
    return (
      <p className="text-sm text-muted">
        У пары нет идей. Идея появляется, когда на паре срабатывает сетап; свою можно посчитать
        ниже.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      {open.length > 0 && (
        <ul aria-label="Идеи пары" className="space-y-2">
          {open.map((idea) => (
            <IdeaItem
              key={idea.id}
              idea={idea}
              selected={idea.id === selectedId}
              onSelect={onSelect}
            />
          ))}
        </ul>
      )}
      {closed.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-xs tracking-wide text-faint uppercase">Истёкшие</h3>
          <ul aria-label="Истёкшие идеи" className="space-y-2 opacity-70">
            {closed.map((idea) => (
              <IdeaItem
                key={idea.id}
                idea={idea}
                selected={idea.id === selectedId}
                onSelect={onSelect}
              />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
