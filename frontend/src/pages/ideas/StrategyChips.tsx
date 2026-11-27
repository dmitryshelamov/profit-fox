import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { countIdeasByStrategy, type IdeaChoices } from '@/api/ideas';

/**
 * The row of strategies above the tables of «Открытые» and «Журнал» (spec strategy-ideas «Ряд стратегий в разделе
 * „Идеи“»; change add-setup-strategies): «Все» with the number of all the ideas and a chip with the name and the number
 * of each strategy of the answer, by name. A chip chooses its strategy; the chosen one or «Все» takes the choice off. The
 * numbers come with the other choices but the strategy, so choosing a chip leaves them as they were, and they are read
 * again with the table.
 */
export function StrategyChips({
  state,
  choices,
  filter,
  refetchInterval,
  onChoose,
}: {
  readonly state: 'open' | 'closed';
  readonly choices: IdeaChoices;
  readonly filter: string | null;
  readonly refetchInterval?: number;
  readonly onChoose: (strategy: string | undefined) => void;
}) {
  const { strategy: chosen, ...others } = choices;
  const counts = useQuery({
    queryKey: ['ideas', 'strategies', state, others, filter],
    queryFn: () => countIdeasByStrategy(state, others, filter ?? undefined),
    placeholderData: keepPreviousData,
    refetchInterval,
  });

  if (!counts.data) {
    return null;
  }

  const chips = [
    {
      key: '',
      words: 'Все',
      count: Number(counts.data.total),
      pressed: chosen === undefined || chosen === '',
    },
    ...counts.data.strategies.map((row) => ({
      key: row.strategy,
      words: row.strategy,
      count: Number(row.ideas),
      pressed: chosen === row.strategy,
    })),
  ];

  return (
    <div role="group" aria-label="Стратегии" className="flex flex-wrap items-center gap-2">
      {chips.map((chip) => (
        <button
          key={chip.key}
          type="button"
          aria-pressed={chip.pressed}
          className={`flex items-center gap-1.5 rounded-full border px-3 py-1 font-mono text-[12px] ${
            chip.pressed
              ? 'border-accent bg-accent text-on-accent'
              : 'border-line bg-chip text-text hover:border-muted'
          }`}
          onClick={() => onChoose(chip.key === '' || chip.pressed ? undefined : chip.key)}
        >
          <span>{chip.words}</span>{' '}
          <span className={chip.pressed ? '' : 'text-muted'}>{chip.count}</span>
        </button>
      ))}
    </div>
  );
}
