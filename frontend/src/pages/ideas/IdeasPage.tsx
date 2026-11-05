import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import {
  type Idea,
  type IdeaFilters,
  type IdeaSort,
  type IdeaSortColumn,
  ideasQuery,
  IdeasTable,
  ideasText,
  ideaTypes,
  loadRisk,
  noIdeaFilters,
  saveRisk,
  sideLabel,
  sortIdeas,
} from '@/modules/ideas';
import { marketQuery, setupChoices, setupIntervals } from '@/modules/market';
import {
  FilterBar,
  FilterEditor,
  filterFieldsQuery,
  filtersOfPlace,
  matchesIdea,
  type SavedFilter,
  savedFiltersQuery,
} from '@/modules/saved-filters';

const control = 'h-9 rounded-lg border border-line bg-panel px-2 text-sm';

const categories = [
  { value: '', label: 'Все' },
  { value: 'linear', label: 'linear' },
  { value: 'spot', label: 'spot' },
];

function Select({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: readonly { value: string; label: string }[];
}) {
  return (
    <select
      aria-label={label}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={control}
    >
      <option value="">{label}: все</option>
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

export function IdeasPage() {
  const [filters, setFilters] = useState<IdeaFilters>(noIdeaFilters);
  const [risk, setRisk] = useState(loadRisk);
  const [sort, setSort] = useState<IdeaSort>({ column: 'expectancy', direction: 'desc' });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<{ filter: SavedFilter | null } | null>(null);
  const ideas = useQuery(ideasQuery(filters));
  const allIdeas = useQuery(ideasQuery());
  const market = useQuery(marketQuery);
  const saved = useQuery(savedFiltersQuery);
  const fields = useQuery(filterFieldsQuery);
  const rows = market.data?.rows ?? [];
  const mine = filtersOfPlace(saved.data ?? [], 'ideas');
  const selected = mine.find((filter) => filter.id === selectedId) ?? null;
  const shown: readonly Idea[] = sortIdeas(
    (ideas.data ?? []).filter(
      (idea) =>
        selected === null ||
        matchesIdea(
          selected,
          idea,
          rows.find((row) => row.category === idea.category && row.symbol === idea.symbol),
        ),
    ),
    sort.column,
    sort.direction,
  );
  // A click on the column of the sorting turns it round; another column starts from the highest value.
  const sortBy = (column: IdeaSortColumn) =>
    setSort((current) => ({
      column,
      direction: current.column === column && current.direction === 'desc' ? 'asc' : 'desc',
    }));
  const set = (patch: Partial<IdeaFilters>) => setFilters((current) => ({ ...current, ...patch }));

  return (
    <section className="space-y-4">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Идеи</h1>
        <p className="text-sm text-muted">
          Идеи по сетапам сканера: открытые и закрытые за последние 24 часа, по ожиданию корзины.
          Только просмотр: отсюда ничего не торгуется.
        </p>
      </header>
      <div className="flex flex-wrap items-center gap-2">
        <Select
          label="Сетап"
          value={filters.setup}
          onChange={(setup) => set({ setup })}
          options={setupChoices}
        />
        <Select
          label="ТФ"
          value={filters.interval}
          onChange={(interval) => set({ interval })}
          options={setupIntervals}
        />
        <Select
          label="Тип идеи"
          value={filters.type}
          onChange={(type) => set({ type })}
          options={ideaTypes}
        />
        <Select
          label="Сторона"
          value={filters.side}
          onChange={(side) => set({ side })}
          options={[
            { value: 'long', label: sideLabel('long') },
            { value: 'short', label: sideLabel('short') },
          ]}
        />
        <div role="group" aria-label="Категория" className="flex rounded-lg border border-line">
          {categories.map((category) => (
            <button
              key={category.value}
              type="button"
              aria-pressed={filters.category === category.value}
              onClick={() => set({ category: category.value })}
              className="h-8 px-3 text-sm text-muted first:rounded-l-lg last:rounded-r-lg hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text"
            >
              {category.label}
            </button>
          ))}
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={filters.nonNegativeExpectancy}
            onChange={(event) => set({ nonNegativeExpectancy: event.target.checked })}
          />
          Ожидание ≥ 0 R
        </label>
        <label className="flex items-center gap-2 text-sm">
          <span className="text-muted">Риск на идею</span>
          <input
            inputMode="decimal"
            aria-label="Риск на идею"
            value={risk}
            onChange={(event) => {
              setRisk(event.target.value);
              saveRisk(event.target.value);
            }}
            className={`${control} w-20 text-right font-mono`}
          />
          <span className="text-muted">USDT</span>
        </label>
        {ideas.data && <p className="ml-auto text-sm text-muted">{ideasText(shown.length)}</p>}
      </div>
      <FilterBar
        filters={mine}
        selected={selected}
        onSelect={setSelectedId}
        onNew={() => setEditing({ filter: null })}
        onEdit={(filter) => setEditing({ filter })}
      />
      {ideas.isError && <p role="alert">Не удалось загрузить идеи.</p>}
      {ideas.isPending && <p className="text-sm text-muted">Загрузка идей…</p>}
      {ideas.data && (
        <>
          {shown.length === 0 ? (
            <p className="text-sm text-muted">Идей по этим условиям нет.</p>
          ) : (
            <>
              <IdeasTable ideas={shown} risk={risk} sort={sort} onSort={sortBy} />
              <ul className="space-y-1 text-xs text-faint">
                <li>
                  Вероятность: доля «Цель» среди «Цель» и «Стоп» случаев корзины за 90 дней; после
                  неё число случаев N. Меньше 30 случаев: «мало данных».
                </li>
                <li>
                  ▲ ≈ ▼: 95 % интервал вероятности выше, пересекает или ниже базовой линии 1 / (1 +
                  R:R).
                </li>
                <li>Прибыль / убыток при риске на идею: прибыль = R:R × риск, убыток = −риск.</li>
              </ul>
            </>
          )}
        </>
      )}
      {editing && fields.data && (
        <FilterEditor
          key={editing.filter?.id ?? 'new'}
          filter={editing.filter}
          place="ideas"
          fields={fields.data}
          rows={rows}
          ideas={allIdeas.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={(id) => {
            setEditing(null);
            setSelectedId(id);
          }}
          onDeleted={(id) => {
            setEditing(null);
            setSelectedId((current) => (current === id ? null : current));
          }}
        />
      )}
    </section>
  );
}
