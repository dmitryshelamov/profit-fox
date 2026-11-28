import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import type { IdeaChoices } from '@/api/ideas';
import { listSignals } from '@/api/signals';
import { listStrategies } from '@/api/strategies';
import { KindChoice } from '@/modules/instrument-kind/KindChoice';
import { timeframes } from '@/preview/preview';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { exitWords } from './ideaColumns';
import { leastScoreOf } from './useMinScore';

/** The label of a burst of ideas: always among the labels to choose (spec idea-labels «Метки в списках идей»). */
export const burstLabel = '⚡ всплеск';

/** The periods of the journal by the time of the exit, in days. */
export const periods = [
  { days: 1, words: 'За сутки' },
  { days: 7, words: 'За 7 дней' },
  { days: 30, words: 'За 30 дней' },
] as const;

/** The labels to choose from: those turned on at signals, and the burst. */
export function useLabelChoices(): string[] {
  const signals = useQuery({ queryKey: ['signals'], queryFn: listSignals });
  const labels = (signals.data ?? []).flatMap((signal) =>
    signal.label?.enabled === true ? [signal.label.text] : [],
  );
  return [...labels.sort((a, b) => a.localeCompare(b)), burstLabel];
}

/**
 * The choices above a table of ideas: part of the symbol, timeframe, side, the kind of the pair (not for the ideas of a
 * pair: change add-instrument-kind-filter), label and «Балл от» (not for the ideas of a pair: change add-idea-score); the
 * open ideas add «только в плюсе»
 * and «только со стопом», the journal the reason of the exit and the period; the ideas of a pair have no search and add
 * the strategy and the state. «Открытые» and «Журнал» choose the strategy by the row of strategies (change
 * add-setup-strategies).
 */
export function IdeaChoiceRow({
  kind,
  choices,
  count,
  onChange,
}: {
  readonly kind: 'open' | 'closed' | 'pair';
  readonly choices: IdeaChoices;
  readonly count?: string;
  readonly onChange: (choices: IdeaChoices) => void;
}) {
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  const labels = useLabelChoices();
  const set = (change: IdeaChoices) => onChange({ ...choices, ...change });

  return (
    <div className="flex flex-wrap items-center gap-3">
      {kind !== 'pair' && (
        <TextField
          sign="⌕"
          aria-label="Поиск по символу"
          placeholder="Поиск по символу"
          className="w-[200px]"
          value={choices.search ?? ''}
          onChange={(event) => set({ search: event.target.value })}
        />
      )}
      {kind === 'pair' && (
        <SelectField
          aria-label="Стратегия"
          className="w-[170px]"
          value={choices.strategy ?? ''}
          onChange={(event) => set({ strategy: event.target.value })}
        >
          <option value="">Стратегия: все</option>
          {(strategies.data ?? [])
            .filter((strategy) => strategy.kind !== 'bot')
            .map((strategy) => (
              <option key={strategy.name} value={strategy.name}>
                {strategy.name}
              </option>
            ))}
        </SelectField>
      )}
      <SelectField
        aria-label="Таймфрейм"
        className="w-[100px]"
        value={choices.timeframe ?? ''}
        onChange={(event) => set({ timeframe: event.target.value })}
      >
        <option value="">ТФ: все</option>
        {timeframes.map((timeframe) => (
          <option key={timeframe} value={timeframe}>
            {timeframe}
          </option>
        ))}
      </SelectField>
      <SelectField
        aria-label="Сторона"
        className="w-[130px]"
        value={choices.side ?? ''}
        onChange={(event) => set({ side: event.target.value })}
      >
        <option value="">Сторона: все</option>
        <option value="long">лонг</option>
        <option value="short">шорт</option>
      </SelectField>
      {kind !== 'pair' && (
        <KindChoice kind={choices.kind} onChange={(chosen) => set({ kind: chosen })} />
      )}
      <SelectField
        aria-label="Метка"
        className="w-[130px]"
        value={choices.label ?? ''}
        onChange={(event) => set({ label: event.target.value })}
      >
        <option value="">Метка: все</option>
        {labels.map((label) => (
          <option key={label} value={label}>
            {label}
          </option>
        ))}
      </SelectField>
      {kind !== 'pair' && (
        <MinScoreField minScore={choices.minScore} onChange={(minScore) => set({ minScore })} />
      )}
      {kind === 'open' && (
        <>
          <Check
            label="Только в плюсе"
            checked={choices.positive === true}
            onChange={(positive) => set({ positive })}
          />
          <Check
            label="Только со стопом"
            checked={choices.withStop === true}
            onChange={(withStop) => set({ withStop })}
          />
        </>
      )}
      {kind === 'closed' && (
        <>
          <SelectField
            aria-label="Чем закрыта"
            className="w-[160px]"
            value={choices.exitReason ?? ''}
            onChange={(event) => set({ exitReason: event.target.value })}
          >
            <option value="">Чем закрыта: все</option>
            {Object.entries(exitWords).map(([reason, words]) => (
              <option key={reason} value={reason}>
                {words}
              </option>
            ))}
          </SelectField>
          <SelectField
            aria-label="Период"
            className="w-[130px]"
            value={choices.period ?? ''}
            onChange={(event) =>
              set({ period: event.target.value === '' ? undefined : Number(event.target.value) })
            }
          >
            <option value="">За всё время</option>
            {periods.map((period) => (
              <option key={period.days} value={period.days}>
                {period.words}
              </option>
            ))}
          </SelectField>
        </>
      )}
      {kind === 'pair' && (
        <SelectField
          aria-label="Статус"
          className="w-[140px]"
          value={choices.state ?? ''}
          onChange={(event) => set({ state: event.target.value })}
        >
          <option value="">Статус: все</option>
          <option value="open">открытые</option>
          <option value="closed">закрытые</option>
        </SelectField>
      )}
      {count !== undefined && (
        <span className="ml-auto font-mono text-[12px] text-muted">{count}</span>
      )}
    </div>
  );
}

function Check({
  label,
  checked,
  onChange,
}: {
  readonly label: string;
  readonly checked: boolean;
  readonly onChange: (checked: boolean) => void;
}) {
  return (
    <label className="flex items-center gap-2 text-[13px] text-muted">
      <input
        type="checkbox"
        className="accent-accent"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      {label}
    </label>
  );
}

/**
 * «Балл от» (change decimal-label-weights, design.md, decision 5): a number with at most two digits after the point; an
 * empty field is no least score. The text typed stays as it is while it is not such a number yet — «-» or «1.» on the way
 * to «-1.5» — and is marked wrong, the least score staying the one before. A least score chosen elsewhere — the other tab,
 * another browser tab — replaces the text.
 */
function MinScoreField({
  minScore,
  onChange,
}: {
  readonly minScore: number | undefined;
  readonly onChange: (minScore: number | undefined) => void;
}) {
  const [text, setText] = useState(minScore === undefined ? '' : String(minScore));
  const [shown, setShown] = useState(minScore);
  if (shown !== minScore) {
    setShown(minScore);
    if (leastScoreOf(text) !== minScore) {
      setText(minScore === undefined ? '' : String(minScore));
    }
  }

  const typed = (next: string, unfinished: boolean) => {
    setText(next);
    const least = leastScoreOf(next);
    if (next.trim() === '' && !unfinished) {
      onChange(undefined);
    } else if (least !== undefined && least !== minScore) {
      onChange(least);
    }
  };
  const wrong = text.trim() !== '' && leastScoreOf(text) === undefined;

  return (
    <TextField
      mono
      type="number"
      step="0.01"
      sign="Балл от"
      placeholder="все"
      aria-label="Балл от"
      aria-invalid={wrong}
      className="w-[150px]"
      value={text}
      onChange={(event) => typed(event.target.value, event.target.validity.badInput)}
    />
  );
}
