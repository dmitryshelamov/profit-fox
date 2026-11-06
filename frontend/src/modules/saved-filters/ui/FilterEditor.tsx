import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';

import { ideasText } from '@/modules/ideas';
import { instrumentsText } from '@/modules/market';
import { ApiError, type Schemas } from '@/shared/api/client';
import { useDebounced } from '@/shared/hooks/useDebounced';

import { fitQuery, useDeleteFilter, useSaveFilter } from '../api';
import {
  choiceLabel,
  fieldLabel,
  fromTyped,
  operatorLabel,
  readyConditions,
  toTyped as toTypedValue,
  unitLabel,
} from '../model/conditions';
import { type Draft, draftOf, switchField } from '../model/draft';
import { filterErrorTexts } from '../model/errors';
import type { SavedFilter } from '../model/savedFilter';

type Field = Schemas['SavedFilterFieldResponse'];

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
    value: draft.operator === 'reliably_positive' ? null : fromTyped(draft.field, draft.value),
    to: draft.operator === 'between' ? fromTyped(draft.field, draft.to) : null,
  }));

/** The field of a condition as a button that opens its catalog in two columns: the fields of the market and of the idea. */
function FieldPicker({
  value,
  fields,
  onPick,
}: {
  value: string;
  fields: readonly Field[];
  onPick: (field: Field) => void;
}) {
  const [open, setOpen] = useState(false);
  const column = (title: string, ofIdea: boolean) => (
    <div role="group" aria-label={title} className="space-y-1">
      <h4 className="px-2 text-[11px] tracking-wide text-faint uppercase">{title}</h4>
      {fields
        .filter((field) => field.ofIdea === ofIdea)
        .map((field) => (
          <button
            key={field.code}
            type="button"
            role="menuitemradio"
            aria-checked={field.code === value}
            onClick={() => {
              setOpen(false);
              onPick(field);
            }}
            className="block w-full rounded px-2 py-1 text-left text-sm whitespace-nowrap hover:bg-panel-2 aria-checked:text-accent"
          >
            {fieldLabel(field.code)}
          </button>
        ))}
    </div>
  );

  return (
    <div
      className="relative"
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) {
          event.stopPropagation();
          setOpen(false);
        }
      }}
    >
      <button
        type="button"
        aria-label="Поле"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
        className={`${control} w-32 truncate text-left`}
      >
        {fieldLabel(value)} ▾
      </button>
      {open && (
        <div
          role="menu"
          aria-label="Поля"
          className="absolute left-0 z-10 mt-1 grid w-max grid-cols-2 gap-4 rounded-lg border border-line bg-panel p-2 shadow-xl"
        >
          {column('Рынок', false)}
          {column('Идея', true)}
        </div>
      )}
    </div>
  );
}

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
    <li className="flex items-center gap-2" aria-label="Условие">
      <FieldPicker
        value={draft.field}
        fields={fields}
        onPick={(next) =>
          onChange(
            switchField(draft, field ?? { code: draft.field, operators: [], choices: null }, next),
          )
        }
      />
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
      {field?.choices ? (
        <select
          aria-label="Значение"
          value={draft.value}
          onChange={(event) => onChange({ ...draft, value: event.target.value })}
          className={`${control} min-w-0 flex-1`}
        >
          {field.choices.map((code) => (
            <option key={code} value={code}>
              {choiceLabel(draft.field, code)}
            </option>
          ))}
        </select>
      ) : (
        draft.operator !== 'reliably_positive' && (
          <input
            aria-label="Значение"
            inputMode="decimal"
            value={draft.value}
            onChange={(event) => onChange({ ...draft, value: event.target.value })}
            className={`${control} w-20 min-w-0`}
          />
        )
      )}
      {draft.operator === 'between' && (
        <>
          <span className="text-xs text-muted">и</span>
          <input
            aria-label="Верхняя граница"
            inputMode="decimal"
            value={draft.to}
            onChange={(event) => onChange({ ...draft, to: event.target.value })}
            className={`${control} w-20 min-w-0`}
          />
        </>
      )}
      {unit !== '' && <span className="text-xs text-muted">{unit}</span>}
      {field?.ofIdea && (
        <span className="rounded bg-panel-2 px-1.5 py-0.5 text-xs text-accent">Идеи</span>
      )}
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
 * The editor of a filter as a side panel: name, place, conditions and a counter of what fits now, which the API counts for
 * the conditions that are ready, after a pause in the typing, without saving anything.
 */
export function FilterEditor({
  filter,
  place,
  fields,
  onClose,
  onSaved,
  onDeleted,
}: {
  /** The filter to change, or `null` for a new one. */
  filter: SavedFilter | null;
  /** Where a new filter is shown by default: the screen that opened the editor. */
  place: 'market' | 'ideas';
  fields: readonly Field[];
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
  // The pause is taken on the text of the ready conditions, which stays the same while nothing is typed.
  const ready = useDebounced(JSON.stringify(readyConditions(stored)), 300);
  const fit = useQuery(fitQuery(useMemo(() => JSON.parse(ready) as typeof stored, [ready])));

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
      className="fixed inset-y-0 right-0 z-20 w-[31rem] max-w-full space-y-4 overflow-y-auto border-l border-line bg-panel p-5 shadow-xl"
    >
      <header className="flex items-center justify-between">
        <h2 className="text-lg font-bold">Фильтр</h2>
        <button
          type="button"
          aria-label="Закрыть"
          onClick={onClose}
          className="h-8 rounded-md px-2 text-muted hover:bg-panel-2"
        >
          ×
        </button>
      </header>
      <label className="block space-y-1 text-sm">
        <span className="text-muted">Название</span>
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          className={`${control} w-full`}
        />
      </label>
      <div className="space-y-1 text-sm">
        <span className="text-muted">Где показывать</span>
        <div
          role="group"
          aria-label="Где показывать"
          className="flex rounded-md border border-line"
        >
          {places.map((item) => (
            <button
              key={item.value}
              type="button"
              aria-pressed={where === item.value}
              onClick={() => setWhere(item.value)}
              className="h-8 flex-1 text-sm text-muted first:rounded-l-md last:rounded-r-md hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text"
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <h3 className="text-sm text-muted">Условия · соединяются через «и»</h3>
          <button
            type="button"
            onClick={() => setDrafts((all) => [...all, draftOf(fields[0]!)])}
            className="text-sm text-accent hover:underline"
          >
            + Условие
          </button>
        </div>
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
      </div>
      <div className="space-y-1">
        <p className="text-sm" aria-live="polite">
          Подходит:{' '}
          {fit.data
            ? `${instrumentsText(Number(fit.data.pairs))} · ${ideasText(Number(fit.data.ideas))}`
            : '…'}
        </p>
        <p className="text-xs text-faint">
          Условия по идеям действуют только в «Идеях»; в «Рынке» их нет.
        </p>
      </div>
      {problems.length > 0 && (
        <ul role="alert" className="space-y-1 rounded-md bg-err-bg p-3 text-sm text-err">
          {problems.map((problem) => (
            <li key={problem}>{problem}</li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-2">
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
            className="h-9 rounded-md px-4 text-sm text-err hover:bg-panel-2"
          >
            Удалить фильтр
          </button>
        )}
        <button
          type="button"
          onClick={onClose}
          className="ml-auto h-9 rounded-md px-4 text-sm hover:bg-panel-2"
        >
          Отмена
        </button>
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
      </div>
    </aside>
  );
}
