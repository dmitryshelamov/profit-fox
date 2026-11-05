import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import {
  type Idea,
  type IdeaFilters,
  ideasQuery,
  IdeasTable,
  ideasText,
  ideaTypes,
  loadRisk,
  noIdeaFilters,
  saveRisk,
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
  const shown: readonly Idea[] = (ideas.data ?? []).filter(
    (idea) =>
      selected === null ||
      matchesIdea(
        selected,
        idea,
        rows.find((row) => row.category === idea.category && row.symbol === idea.symbol),
      ),
  );
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
          label="Тип"
          value={filters.type}
          onChange={(type) => set({ type })}
          options={ideaTypes}
        />
        <Select
          label="Сторона"
          value={filters.side}
          onChange={(side) => set({ side })}
          options={[
            { value: 'long', label: 'Long' },
            { value: 'short', label: 'Short' },
          ]}
        />
        <Select
          label="Категория"
          value={filters.category}
          onChange={(category) => set({ category })}
          options={[
            { value: 'linear', label: 'linear' },
            { value: 'spot', label: 'spot' },
          ]}
        />
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            role="switch"
            checked={filters.nonNegativeExpectancy}
            onChange={(event) => set({ nonNegativeExpectancy: event.target.checked })}
          />
          Ожидание ≥ 0 R
        </label>
        <label className="ml-auto flex items-center gap-2 text-sm">
          <span className="text-muted">Риск на идею</span>
          <input
            inputMode="decimal"
            aria-label="Риск на идею"
            placeholder="USDT"
            value={risk}
            onChange={(event) => {
              setRisk(event.target.value);
              saveRisk(event.target.value);
            }}
            className={`${control} w-28 text-right font-mono`}
          />
          <span className="text-muted">USDT</span>
        </label>
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
          <p className="text-sm text-muted">{ideasText(shown.length)}</p>
          {shown.length === 0 ? (
            <p className="text-sm text-muted">Идей по этим условиям нет.</p>
          ) : (
            <IdeasTable ideas={shown} risk={risk} />
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
