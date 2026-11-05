import { useState } from 'react';

import { intervalLabel } from '@/modules/market-data';

import { groupPairIdeas } from '../model/groups';
import { typeLabel } from '../model/labels';
import { priceByTick } from '../model/price';
import { loadRisk } from '../model/riskStorage';
import { expectancyInUsdt, riskOutcome } from '../model/stats';
import {
  ExpectancyCell,
  type Idea,
  ProbabilityCell,
  SetupBadge,
  SideText,
  StatusText,
} from './IdeaCells';

function IdeaItem({
  idea,
  risk,
  selected,
  onSelect,
}: {
  idea: Idea;
  risk: string;
  selected: boolean;
  onSelect: (idea: Idea) => void;
}) {
  const outcome = riskOutcome(idea.riskReward, risk);
  const expectancy = expectancyInUsdt(idea.bucket, risk);

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
          <span>Вход {priceByTick(idea.entry, idea.tick)}</span>
          <span>Стоп {priceByTick(idea.stop, idea.tick)}</span>
          <span>Цель {priceByTick(idea.target, idea.tick)}</span>
        </span>
        {outcome !== null && (
          <span className="flex flex-wrap gap-x-3 font-mono text-xs text-muted">
            <span>
              Прибыль <span className="text-ok">{outcome.profit}</span>
            </span>
            <span>
              Убыток <span className="text-err">{outcome.loss}</span>
            </span>
            {expectancy !== null && <span>Ожидание {expectancy}</span>}
            <span>USDT</span>
          </span>
        )}
        <span className="flex flex-wrap gap-x-3 text-xs text-muted">
          <ProbabilityCell idea={idea} />
          <span className="font-mono">
            <ExpectancyCell idea={idea} />
          </span>
          <span className="text-faint">{intervalLabel(idea.interval)}</span>
        </span>
      </button>
    </li>
  );
}

/**
 * The ideas of a pair: those waiting for entry and the active ones first, in the order the API gave them (by expectancy),
 * then the group of the expired ones, closed in the last 24 hours. The selected idea is marked. Each card shows its money
 * for the risk per idea that the owner set on the screen of the ideas.
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
  const [risk] = useState(loadRisk);

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
              risk={risk}
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
                risk={risk}
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
