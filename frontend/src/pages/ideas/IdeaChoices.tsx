import { useQuery } from '@tanstack/react-query';

import type { IdeaChoices } from '@/api/ideas';
import { listSignals } from '@/api/signals';
import { listStrategies } from '@/api/strategies';
import { timeframes } from '@/preview/preview';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { exitWords } from './ideaColumns';

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
 * The choices above a table of ideas: part of the symbol, strategy, timeframe, side and label; the open ideas add «только
 * в плюсе» and «только со стопом», the journal the reason of the exit and the period; the ideas of a pair have no search
 * and add the state.
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
