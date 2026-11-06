import { useState } from 'react';

import { loadAmount } from '../model/amountStorage';
import { groupPairIdeas } from '../model/groups';
import type { Idea } from './IdeaCells';
import { PairIdeasTable } from './PairIdeasTable';

/**
 * The ideas of a pair: those waiting for entry and the active ones first, in the order the API gave them (by expectancy),
 * then the group of the expired ones, closed in the last 24 hours. Each row shows its money for the amount per idea that the
 * owner set on the screen of the ideas.
 */
export function PairIdeasList({
  ideas,
  selectedId,
  onSelect,
  onEnter,
}: {
  ideas: readonly Idea[];
  selectedId: string | undefined;
  onSelect: (idea: Idea) => void;
  /** Enters a trade by an idea that waits for its entry or is active; the rows of the others have no button. */
  onEnter?: (idea: Idea) => void;
}) {
  const { open, closed } = groupPairIdeas(ideas);
  const [amount] = useState(loadAmount);

  if (ideas.length === 0) {
    return (
      <p className="text-sm text-muted">
        У пары нет идей. Идея появляется, когда на паре срабатывает сетап; свою можно посчитать
        ниже.
      </p>
    );
  }

  return (
    <PairIdeasTable
      open={open}
      expired={closed}
      amount={amount}
      selectedId={selectedId}
      onSelect={onSelect}
      onEnter={onEnter}
    />
  );
}
