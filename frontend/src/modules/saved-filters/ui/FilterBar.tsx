import { conditionText } from '../model/conditions';
import type { SavedFilter } from '../model/savedFilter';

export type FilterPlace = 'market' | 'ideas';

/** The filters shown in a place: those saved for it and those saved for both. */
export const filtersOfPlace = (filters: readonly SavedFilter[], place: FilterPlace) =>
  filters.filter((filter) => filter.place === place || filter.place === 'both');

/**
 * The row «Фильтры»: a chip for each saved filter of the place and «+ Новый фильтр». The chosen filter shows its
 * conditions in a line with «Изменить» and «Снять».
 */
export function FilterBar({
  filters,
  selected,
  onSelect,
  onNew,
  onEdit,
}: {
  filters: readonly SavedFilter[];
  selected: SavedFilter | null;
  onSelect: (id: string | null) => void;
  onNew: () => void;
  onEdit: (filter: SavedFilter) => void;
}) {
  return (
    <div className="space-y-2">
      <div role="group" aria-label="Фильтры" className="flex flex-wrap items-center gap-2">
        <span className="text-xs text-faint">Фильтры</span>
        {filters.map((filter) => (
          <button
            key={filter.id}
            type="button"
            aria-pressed={filter.id === selected?.id}
            onClick={() => onSelect(filter.id === selected?.id ? null : filter.id)}
            className="h-7 rounded-full border border-line bg-panel px-3 text-xs text-muted hover:bg-panel-2 aria-pressed:border-accent aria-pressed:text-text"
          >
            {filter.name}
          </button>
        ))}
        <button
          type="button"
          onClick={onNew}
          className="h-7 rounded-full border border-dashed border-line px-3 text-xs text-accent hover:bg-panel-2"
        >
          + Новый фильтр
        </button>
      </div>
      {selected && (
        <p className="flex flex-wrap items-center gap-x-3 text-xs text-muted">
          <span aria-label="Условия фильтра">
            {selected.conditions.map(conditionText).join(' и ')}
          </span>
          <button
            type="button"
            onClick={() => onEdit(selected)}
            className="text-accent hover:underline"
          >
            Изменить
          </button>
          <button
            type="button"
            onClick={() => onSelect(null)}
            className="text-accent hover:underline"
          >
            Снять
          </button>
        </p>
      )}
    </div>
  );
}
