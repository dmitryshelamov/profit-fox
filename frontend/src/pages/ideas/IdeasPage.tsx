import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  type IdeaFilters,
  type IdeaSort,
  type IdeaSortColumn,
  ideasQuery,
  IdeasTable,
  ideasText,
  ideaTypes,
  loadAmount,
  loadIdeasChoice,
  saveAmount,
  saveIdeasChoice,
  sideLabel,
} from '@/modules/ideas';
import { setupChoices, setupIntervals } from '@/modules/market';
import {
  FilterBar,
  FilterEditor,
  filterFieldsQuery,
  filtersOfPlace,
  type SavedFilter,
  savedFiltersQuery,
} from '@/modules/saved-filters';
import { Pager } from '@/shared/ui/Pager';

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

/** The ideas of the scanner, one page at a time: the API filters, sorts and cuts the list, the address holds the number of the page. */
export function IdeasPage({ page }: { page: number }) {
  const navigate = useNavigate();
  // What the owner chose last time; the saved filter that is gone since selects nothing.
  const [remembered] = useState(loadIdeasChoice);
  const [filters, setFilters] = useState<IdeaFilters>(remembered.filters);
  const [amount, setAmount] = useState(loadAmount);
  const [sort, setSort] = useState<IdeaSort>(remembered.sort);
  const [selectedId, setSelectedId] = useState<string | null>(remembered.savedFilterId);
  const [pageSize, setPageSize] = useState(remembered.pageSize);
  const [editing, setEditing] = useState<{ filter: SavedFilter | null } | null>(null);
  const saved = useQuery(savedFiltersQuery);
  const fields = useQuery(filterFieldsQuery);
  const mine = filtersOfPlace(saved.data ?? [], 'ideas');
  const selected = mine.find((filter) => filter.id === selectedId) ?? null;
  // A saved filter that is remembered waits for the list of filters, so one that is gone is never asked for.
  const ideas = useQuery(
    ideasQuery(
      { filters, savedFilterId: selected?.id ?? null, sort, page, pageSize },
      selectedId === null || saved.isFetched,
    ),
  );
  const answered = ideas.data?.page;
  const showPage = (number: number, replace = false) =>
    void navigate({ to: '/ideas', search: { page: number === 1 ? undefined : number }, replace });
  // Any other choice starts the list over from its first page.
  const firstPage = () => {
    if (page !== 1) {
      showPage(1, true);
    }
  };
  // The list may have become shorter: the address follows the page the API gave.
  useEffect(() => {
    if (answered !== undefined && !ideas.isPlaceholderData && Number(answered) !== page) {
      showPage(Number(answered), true);
    }
  }, [answered, ideas.isPlaceholderData, page]);
  // A click on the column of the sorting turns it round; another column starts from the highest value.
  const sortBy = (column: IdeaSortColumn) => {
    setSort((current) => ({
      column,
      direction: current.column === column && current.direction === 'desc' ? 'asc' : 'desc',
    }));
    firstPage();
  };
  useEffect(
    () => saveIdeasChoice({ filters, savedFilterId: selectedId, sort, pageSize }),
    [filters, selectedId, sort, pageSize],
  );
  const set = (patch: Partial<IdeaFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
    firstPage();
  };
  const select = (id: string | null) => {
    setSelectedId(id);
    firstPage();
  };

  return (
    <section className="space-y-4">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Идеи</h1>
        <p className="text-sm text-muted">
          Идеи по сетапам сканера: открытые и закрытые за последние 24 часа, по ожиданию корзины.
          Войти по идее можно со страницы пары.
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
          <span className="text-muted">Сумма на идею</span>
          <input
            inputMode="decimal"
            aria-label="Сумма на идею"
            value={amount}
            onChange={(event) => {
              setAmount(event.target.value);
              saveAmount(event.target.value);
            }}
            className={`${control} w-20 text-right font-mono`}
          />
          <span className="text-muted">USDT</span>
        </label>
        {ideas.data && (
          <p className="ml-auto text-sm text-muted">{ideasText(Number(ideas.data.total))}</p>
        )}
      </div>
      <FilterBar
        filters={mine}
        selected={selected}
        onSelect={select}
        onNew={() => setEditing({ filter: null })}
        onEdit={(filter) => setEditing({ filter })}
      />
      {ideas.isError && <p role="alert">Не удалось загрузить идеи.</p>}
      {ideas.isPending && <p className="text-sm text-muted">Загрузка идей…</p>}
      {ideas.data && (
        <>
          {ideas.data.items.length === 0 ? (
            <p className="text-sm text-muted">Идей по этим условиям нет.</p>
          ) : (
            <>
              <IdeasTable ideas={ideas.data.items} amount={amount} sort={sort} onSort={sortBy} />
              <Pager
                page={Number(ideas.data.page)}
                pageSize={Number(ideas.data.pageSize)}
                total={Number(ideas.data.total)}
                onPage={showPage}
                onPageSize={(size) => {
                  setPageSize(size);
                  firstPage();
                }}
              />
              <ul className="space-y-1 text-xs text-faint">
                <li>
                  Вероятность: доля «Цель» среди «Цель» и «Стоп» случаев корзины за 90 дней; после
                  неё число случаев N. Меньше 30 случаев: «мало данных».
                </li>
                <li>
                  ▲ ≈ ▼: 95 % интервал вероятности выше, пересекает или ниже базовой линии 1 / (1 +
                  R:R).
                </li>
                <li>Прибыль / убыток на сумму на идею: при цели и при стопе, после комиссий.</li>
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
          onClose={() => setEditing(null)}
          onSaved={(id) => {
            setEditing(null);
            select(id);
          }}
          onDeleted={(id) => {
            setEditing(null);
            if (selectedId === id) {
              select(null);
            }
          }}
        />
      )}
    </section>
  );
}
