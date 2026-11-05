import { useState } from 'react';

import { type Idea, ideasText } from '@/modules/ideas';
import { instrumentsText, type MarketRow } from '@/modules/market';
import { ApiError, type Schemas } from '@/shared/api/client';

import { useDeleteFilter, useSaveFilter } from '../api';
import { matchesIdea, matchesMarketRow, type SavedFilter } from '../model/apply';
import {
  fieldLabel,
  fromTyped,
  operatorLabel,
  readyConditions,
  toTyped as toTypedValue,
  unitLabel,
} from '../model/conditions';
import { filterErrorTexts } from '../model/errors';

type Field = Schemas['SavedFilterFieldResponse'];

/** A condition as the owner types it: percent for funding and probability, the numbers themselves otherwise. */
interface Draft {
  field: string;
  operator: string;
  value: string;
  to: string;
}

const control = 'h-8 rounded-md border border-line bg-panel-2 px-2 text-sm';

const places = [
  { value: 'market', label: 'Рынок' },
  { value: 'ideas', label: 'Идеи' },
  { value: 'both', label: 'Оба' },
];

const toStored = (drafts: readonly Draft[]) =>
  drafts.map((draft) => ({
    field: draft.field,
    operator: draft.operator,
    value: draft.operator === 'above_baseline' ? null : fromTyped(draft.field, draft.value),
    to: draft.operator === 'between' ? fromTyped(draft.field, draft.to) : null,
  }));

function DraftRow({
  draft,
  fields,
  onChange,
  onRemove,
}: {
  draft: Draft;
  fields: readonly Field[];
  onChange: (draft: Draft) => void;
  onRemove: () => void;
}) {
  const field = fields.find((item) => item.code === draft.field);
  const unit = unitLabel(draft.field);

  return (
    <li className="flex flex-wrap items-center gap-2" aria-label="Условие">
      <select
        aria-label="Поле"
        value={draft.field}
        onChange={(event) => {
          const next = fields.find((item) => item.code === event.target.value)!;

          onChange({
            ...draft,
            field: next.code,
            operator: next.operators.includes(draft.operator) ? draft.operator : next.operators[0]!,
          });
        }}
        className={control}
      >
        {fields.map((item) => (
          <option key={item.code} value={item.code}>
            {fieldLabel(item.code)}
            {item.ofIdea ? ' · Идеи' : ''}
          </option>
        ))}
      </select>
      {field?.ofIdea && (
        <span className="rounded bg-panel-2 px-1.5 py-0.5 text-xs text-accent">Идеи</span>
      )}
      <select
        aria-label="Оператор"
        value={draft.operator}
        onChange={(event) => onChange({ ...draft, operator: event.target.value })}
        className={control}
      >
        {(field?.operators ?? []).map((operator) => (
          <option key={operator} value={operator}>
            {operatorLabel(operator)}
          </option>
        ))}
      </select>
      {draft.operator !== 'above_baseline' && (
        <input
          aria-label="Значение"
          inputMode="decimal"
          value={draft.value}
          onChange={(event) => onChange({ ...draft, value: event.target.value })}
          className={`${control} w-24`}
        />
      )}
      {draft.operator === 'between' && (
        <>
          <span className="text-xs text-muted">и</span>
          <input
            aria-label="Верхняя граница"
            inputMode="decimal"
            value={draft.to}
            onChange={(event) => onChange({ ...draft, to: event.target.value })}
            className={`${control} w-24`}
          />
        </>
      )}
      {unit !== '' && <span className="text-xs text-muted">{unit}</span>}
      <button
        type="button"
        aria-label="Удалить условие"
        onClick={onRemove}
        className="ml-auto h-8 rounded-md px-2 text-muted hover:bg-panel-2"
      >
        ×
      </button>
    </li>
  );
}

/**
 * The editor of a filter as a side panel: name, place, conditions and a counter of what fits now, recomputed from the
 * snapshot and the ideas the screen holds without saving anything.
 */
export function FilterEditor({
  filter,
  place,
  fields,
  rows,
  ideas,
  onClose,
  onSaved,
  onDeleted,
}: {
  /** The filter to change, or `null` for a new one. */
  filter: SavedFilter | null;
  /** Where a new filter is shown by default: the screen that opened the editor. */
  place: 'market' | 'ideas';
  fields: readonly Field[];
  rows: readonly MarketRow[];
  ideas: readonly Idea[];
  onClose: () => void;
  /** The id of the filter saved. */
  onSaved: (id: string) => void;
  onDeleted: (id: string) => void;
}) {
  const [name, setName] = useState(filter?.name ?? '');
  const [where, setWhere] = useState(filter?.place ?? place);
  const [drafts, setDrafts] = useState<Draft[]>(() =>
    (filter?.conditions ?? []).map((condition) => ({
      field: condition.field!,
      operator: condition.operator!,
      value: condition.value === null ? '' : toTypedValue(condition.field!, condition.value),
      to: condition.to === null ? '' : toTypedValue(condition.field!, condition.to),
    })),
  );
  const [problems, setProblems] = useState<string[]>([]);
  const save = useSaveFilter();
  const remove = useDeleteFilter();

  const stored = toStored(drafts);
  const trial: SavedFilter = {
    id: filter?.id ?? '',
    name,
    place: where,
    conditions: readyConditions(stored),
  };
  const rowOf = (idea: Idea) =>
    rows.find((row) => row.category === idea.category && row.symbol === idea.symbol);
  const fitRows = rows.filter((row) => matchesMarketRow(trial, row)).length;
  const fitIdeas = ideas.filter((idea) => matchesIdea(trial, idea, rowOf(idea))).length;

  const failed = (error: Error) =>
    setProblems(
      error instanceof ApiError && Object.keys(error.errors).length > 0
        ? filterErrorTexts(error.errors)
        : ['Не удалось сохранить фильтр.'],
    );

  return (
    <aside
      role="dialog"
      aria-label="Редактор фильтра"
      className="fixed inset-y-0 right-0 z-20 w-[28rem] max-w-full space-y-4 overflow-y-auto border-l border-line bg-panel p-5 shadow-xl"
    >
      <h2 className="text-lg font-bold">{filter ? 'Изменить фильтр' : 'Новый фильтр'}</h2>
      <label className="block space-y-1 text-sm">
        <span className="text-muted">Имя</span>
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          className={`${control} w-full`}
        />
      </label>
      <label className="block space-y-1 text-sm">
        <span className="text-muted">Где показывать</span>
        <select
          value={where}
          onChange={(event) => setWhere(event.target.value)}
          className={`${control} w-full`}
        >
          {places.map((item) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
        </select>
      </label>
      <div className="space-y-2">
        <h3 className="text-sm text-muted">Условия</h3>
        <ul className="space-y-2">
          {drafts.map((draft, index) => (
            <DraftRow
              key={index}
              draft={draft}
              fields={fields}
              onChange={(next) =>
                setDrafts((all) => all.map((item, at) => (at === index ? next : item)))
              }
              onRemove={() => setDrafts((all) => all.filter((_, at) => at !== index))}
            />
          ))}
        </ul>
        <button
          type="button"
          onClick={() =>
            setDrafts((all) => [
              ...all,
              { field: fields[0]!.code, operator: fields[0]!.operators[0]!, value: '', to: '' },
            ])
          }
          className="text-sm text-accent hover:underline"
        >
          + Условие
        </button>
      </div>
      <p className="text-sm" aria-live="polite">
        Подходит: {instrumentsText(fitRows)} · {ideasText(fitIdeas)}
      </p>
      {problems.length > 0 && (
        <ul role="alert" className="space-y-1 rounded-md bg-err-bg p-3 text-sm text-err">
          {problems.map((problem) => (
            <li key={problem}>{problem}</li>
          ))}
        </ul>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={save.isPending || readyConditions(stored).length !== stored.length}
          onClick={() =>
            save.mutate(
              { id: filter?.id ?? null, body: { name, place: where, conditions: stored } },
              { onSuccess: onSaved, onError: failed },
            )
          }
          className="h-9 rounded-md bg-accent px-4 text-sm font-semibold text-on-accent disabled:opacity-50"
        >
          Сохранить
        </button>
        <button
          type="button"
          onClick={onClose}
          className="h-9 rounded-md px-4 text-sm hover:bg-panel-2"
        >
          Отмена
        </button>
        {filter && (
          <button
            type="button"
            disabled={remove.isPending}
            onClick={() =>
              remove.mutate(filter.id, {
                onSuccess: () => onDeleted(filter.id),
                onError: failed,
              })
            }
            className="ml-auto h-9 rounded-md px-4 text-sm text-err hover:bg-panel-2"
          >
            Удалить фильтр
          </button>
        )}
      </div>
    </aside>
  );
}
