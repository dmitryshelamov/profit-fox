import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { type FilterPlace, listFilterFields, listFilters, type SavedFilter } from '@/api/filters';

import { FilterEditor } from './FilterEditor';
import type { ListPlace } from './useChosenFilter';
import { filterWords } from './words';

/**
 * The row «Фильтры» above a table of «Рынок» or «Идеи» (spec saved-filters «Строка фильтров»): a badge for each filter
 * of the place — a click chooses it, a second one takes it off — and «+ Новый фильтр». The chosen filter shows its
 * conditions joined by «и» with «Изменить» and «Снять».
 */
export function FilterRow({
  place,
  chosen,
  onChoose,
}: {
  readonly place: ListPlace;
  readonly chosen: string | null;
  readonly onChoose: (id: string | null) => void;
}) {
  const filters = useQuery({ queryKey: ['filters'], queryFn: listFilters });
  const fields = useQuery({ queryKey: ['filter-fields'], queryFn: listFilterFields });
  const [editing, setEditing] = useState<SavedFilter | 'new' | null>(null);
  const mine = (filters.data ?? []).filter(
    (filter) => filter.place === place || filter.place === 'both',
  );
  const current = mine.find((filter) => filter.id === chosen) ?? null;
  const chip = 'h-7 cursor-pointer rounded-full border px-3 text-[12px]';

  return (
    <div className="flex flex-col gap-2">
      <div role="group" aria-label="Фильтры" className="flex flex-wrap items-center gap-2">
        <span className="mr-2 text-[12px] font-semibold text-muted">Фильтры</span>
        {mine.map((filter) => (
          <button
            key={filter.id}
            type="button"
            aria-pressed={filter.id === chosen}
            className={`${chip} ${filter.id === chosen ? 'border-accent bg-accent/15 font-semibold text-accent' : 'border-line text-text hover:border-muted'}`}
            onClick={() => onChoose(filter.id === chosen ? null : filter.id)}
          >
            {filter.name}
          </button>
        ))}
        <button
          type="button"
          className={`${chip} border-dashed border-line text-muted hover:text-text`}
          onClick={() => setEditing('new')}
        >
          + Новый фильтр
        </button>
      </div>
      {current !== null && (
        <p
          aria-label="Условия фильтра"
          className="flex flex-wrap items-center gap-5 pl-[68px] text-[12px] text-muted"
        >
          <span>{filterWords(current.conditions, fields.data ?? [])}</span>
          <button
            type="button"
            className="cursor-pointer text-accent hover:underline"
            onClick={() => setEditing(current)}
          >
            Изменить
          </button>
          <button
            type="button"
            className="cursor-pointer text-accent hover:underline"
            onClick={() => onChoose(null)}
          >
            Снять
          </button>
        </p>
      )}
      {editing !== null && (
        <FilterEditor
          filter={editing === 'new' ? null : editing}
          place={place as FilterPlace}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null);
            onChoose(saved.place === place || saved.place === 'both' ? saved.id : null);
          }}
          onDeleted={(id) => {
            setEditing(null);
            if (id === chosen) {
              onChoose(null);
            }
          }}
        />
      )}
    </div>
  );
}
