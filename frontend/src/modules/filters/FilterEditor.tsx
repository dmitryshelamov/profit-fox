import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import type { RequestError } from '@/api/client';
import {
  countFilter,
  deleteFilter,
  type FilterCondition,
  type FilterCount,
  type FilterPlace,
  listFilterFields,
  type SavedFilter,
  saveFilter,
} from '@/api/filters';
import { Button } from '@/shared/ui/Button';
import { Segmented } from '@/shared/ui/Segmented';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { operatorSigns, operatorsOf, valueWord } from './words';

/** How long the editor waits after a change before it counts the rows again. */
export const countDelayMs = 300;

/** The first parts of the keys of the lists a filter of a place picks rows of: «Рынок» and the three of «Идеи». */
const listKeys: Readonly<Record<string, readonly string[]>> = {
  market: ['market'],
  ideas: ['ideas'],
  both: ['market', 'ideas'],
};

const places: readonly { value: FilterPlace; label: string }[] = [
  { value: 'market', label: 'Рынок' },
  { value: 'ideas', label: 'Идеи' },
  { value: 'both', label: 'Оба' },
];

/**
 * The side panel of a filter (spec saved-filters «Редактор фильтра»): its name, its place and its conditions joined by
 * «и», each added and removed; the count of the rows the draft matches, asked again on each change; «Сохранить»,
 * «Отмена» and, for a saved filter, «Удалить фильтр». A refusal stays in the panel at its field or condition.
 */
export function FilterEditor({
  filter,
  place,
  onSaved,
  onDeleted,
  onClose,
}: {
  /** The filter to change; null for a new one. */
  readonly filter: SavedFilter | null;
  /** The place a new filter starts with. */
  readonly place: FilterPlace;
  readonly onSaved: (filter: SavedFilter) => void;
  readonly onDeleted: (id: string) => void;
  readonly onClose: () => void;
}) {
  const queries = useQueryClient();
  const fields = useQuery({ queryKey: ['filter-fields'], queryFn: listFilterFields });
  const [name, setName] = useState(filter?.name ?? '');
  const [where, setWhere] = useState<FilterPlace>(
    (filter?.place as FilterPlace | undefined) ?? place,
  );
  const [conditions, setConditions] = useState<FilterCondition[]>(
    filter?.conditions.map((condition) => ({ ...condition })) ?? [
      { field: 'fundingRate', operator: 'ge', value: '' },
    ],
  );
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [count, setCount] = useState<FilterCount | null>(null);
  const [countErrors, setCountErrors] = useState<readonly RequestError[]>([]);
  const all = fields.data ?? [];

  useEffect(() => {
    const filled = conditions.filter((condition) => (condition.value ?? '') !== '');
    if (filled.length === 0) {
      setCount(null);
      setCountErrors([]);
      return;
    }

    let current = true;
    const timer = setTimeout(() => {
      void countFilter(filled).then((result) => {
        if (!current) {
          return;
        }

        setCount(result.ok ? result.value : null);
        setCountErrors(result.ok ? [] : result.errors);
      });
    }, countDelayMs);
    return () => {
      current = false;
      clearTimeout(timer);
    };
  }, [conditions]);

  const change = (index: number, next: Partial<FilterCondition>) =>
    setConditions((old) =>
      old.map((condition, at) => (at === index ? { ...condition, ...next } : condition)),
    );
  const errorsAt = (index: number) =>
    errors.filter((error) => error.field === 'conditions' && error.condition === index);
  const fieldErrors = (field: string) => errors.filter((error) => error.field === field);

  const save = async () => {
    const result = await saveFilter(filter?.id ?? null, name, where, conditions);
    if (!result.ok) {
      setErrors(result.errors);
      return;
    }

    await queries.invalidateQueries({ queryKey: ['filters'] });
    await invalidateLists([where, filter?.place ?? where]);
    onSaved(result.value);
  };

  // The lists of the places of the filter are read again: the same filter now picks other rows (task 18.21).
  const invalidateLists = async (places: readonly string[]) => {
    const keys = new Set(places.flatMap((place) => listKeys[place] ?? []));
    await Promise.all([...keys].map((key) => queries.invalidateQueries({ queryKey: [key] })));
  };

  const remove = async () => {
    if (filter === null) {
      return;
    }

    await deleteFilter(filter.id);
    await queries.invalidateQueries({ queryKey: ['filters'] });
    await invalidateLists([filter.place]);
    onDeleted(filter.id);
  };

  const matched =
    count === null
      ? null
      : where === 'ideas'
        ? `${Number(count.ideas)} из ${Number(count.allIdeas)} идей`
        : where === 'market'
          ? `${Number(count.pairs)} из ${Number(count.allPairs)}`
          : `${Number(count.pairs)} из ${Number(count.allPairs)} пар · ${Number(count.ideas)} из ${Number(count.allIdeas)} идей`;

  return (
    <aside
      role="dialog"
      aria-label="Фильтр"
      className="fixed inset-y-0 right-0 z-30 flex w-[480px] max-w-full flex-col border-l border-line bg-panel shadow-2xl"
    >
      <header className="flex items-center justify-between px-6 pt-6">
        <h2 className="text-base font-bold">Фильтр</h2>
        <button
          type="button"
          aria-label="Закрыть"
          className="cursor-pointer text-muted hover:text-text"
          onClick={onClose}
        >
          ×
        </button>
      </header>
      <div className="flex flex-1 flex-col gap-5 overflow-y-auto px-6 py-5">
        <label className="flex flex-col gap-1.5 text-[11px] font-semibold text-muted uppercase">
          Название
          <TextField
            aria-label="Название"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
          <Problems errors={fieldErrors('name')} label="Ошибка названия" />
        </label>
        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-semibold text-muted uppercase">Где показывать</span>
          <Segmented label="Где показывать" value={where} options={places} onChange={setWhere} />
          <Problems errors={fieldErrors('place')} label="Ошибка места" />
        </div>
        <div className="flex flex-col gap-2">
          <span className="text-[11px] font-semibold text-muted uppercase">
            Условия · соединяются через «и»
          </span>
          {conditions.map((condition, index) => {
            const field = all.find((candidate) => candidate.name === condition.field);
            const operators = operatorsOf(field);
            return (
              <div
                key={index}
                role="group"
                aria-label={`Условие ${index + 1}`}
                className="flex flex-col gap-1"
              >
                <span className="flex items-center gap-2">
                  <SelectField
                    aria-label="Поле"
                    className="w-[180px]"
                    value={condition.field}
                    onChange={(event) => {
                      const next = all.find((candidate) => candidate.name === event.target.value);
                      change(index, {
                        field: event.target.value,
                        operator:
                          operatorsOf(next)[0] === 'eq' && next?.type !== 'number'
                            ? 'eq'
                            : condition.operator,
                        value: next?.values?.[0] ?? '',
                        to: null,
                      });
                    }}
                  >
                    {(['market', 'idea'] as const).map((group) => (
                      <optgroup key={group} label={group === 'market' ? 'Рынок' : 'Идея'}>
                        {all
                          .filter((candidate) => candidate.place === group)
                          .map((candidate) => (
                            <option key={candidate.name} value={candidate.name}>
                              {candidate.title}
                            </option>
                          ))}
                      </optgroup>
                    ))}
                  </SelectField>
                  <SelectField
                    aria-label="Оператор"
                    className="w-[90px]"
                    value={condition.operator}
                    onChange={(event) => change(index, { operator: event.target.value, to: null })}
                  >
                    {operators.map((operator) => (
                      <option key={operator} value={operator}>
                        {operatorSigns[operator]}
                      </option>
                    ))}
                  </SelectField>
                  {field?.values ? (
                    <SelectField
                      aria-label="Значение"
                      className="w-[110px]"
                      value={condition.value ?? ''}
                      onChange={(event) => change(index, { value: event.target.value })}
                    >
                      {field.values.map((value) => (
                        <option key={value} value={value}>
                          {valueWord(value)}
                        </option>
                      ))}
                    </SelectField>
                  ) : (
                    <TextField
                      aria-label="Значение"
                      mono
                      className="w-[90px]"
                      value={condition.value ?? ''}
                      onChange={(event) => change(index, { value: event.target.value })}
                    />
                  )}
                  {condition.operator === 'between' && (
                    <TextField
                      aria-label="До"
                      mono
                      className="w-[80px]"
                      value={condition.to ?? ''}
                      onChange={(event) => change(index, { to: event.target.value })}
                    />
                  )}
                  <span className="w-[56px] text-[12px] text-muted">{field?.unit ?? ''}</span>
                  <button
                    type="button"
                    aria-label={`Убрать условие ${index + 1}`}
                    className="cursor-pointer text-muted hover:text-text"
                    onClick={() => setConditions((old) => old.filter((_, at) => at !== index))}
                  >
                    ×
                  </button>
                </span>
                <Problems errors={errorsAt(index)} label={`Ошибка условия ${index + 1}`} />
              </div>
            );
          })}
          <Problems
            errors={fieldErrors('conditions').filter((error) => error.condition == null)}
            label="Ошибка условий"
          />
          <span>
            <Button
              size="sm"
              onClick={() =>
                setConditions((old) => [
                  ...old,
                  { field: 'fundingRate', operator: 'ge', value: '' },
                ])
              }
            >
              + Условие
            </Button>
          </span>
        </div>
        <p
          role="status"
          aria-label="Подходит"
          className={countErrors.length > 0 ? 'text-err' : 'font-semibold text-ok'}
        >
          {countErrors.length > 0
            ? countErrors.map((error) => error.reason).join('; ')
            : matched === null
              ? ''
              : `Подходит: ${matched}`}
        </p>
      </div>
      <footer className="flex items-center gap-3 border-t border-line px-6 py-4">
        {filter !== null && (
          <Button variant="plain" className="text-err!" onClick={() => void remove()}>
            Удалить фильтр
          </Button>
        )}
        <span className="ml-auto flex gap-3">
          <Button onClick={onClose}>Отмена</Button>
          <Button variant="primary" onClick={() => void save()}>
            Сохранить
          </Button>
        </span>
      </footer>
    </aside>
  );
}

function Problems({
  errors,
  label,
}: {
  readonly errors: readonly RequestError[];
  readonly label: string;
}) {
  return errors.length === 0 ? null : (
    <span role="alert" aria-label={label} className="text-[12px] font-normal text-err normal-case">
      {errors.map((error) => error.reason).join('; ')}
    </span>
  );
}
