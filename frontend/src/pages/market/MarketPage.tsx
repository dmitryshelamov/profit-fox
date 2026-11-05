import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { ideasQuery } from '@/modules/ideas';
import {
  type CategoryFilter,
  categoryOptions,
  filterBySetup,
  filterRows,
  isStale,
  loadMarketChoice,
  marketQuery,
  MarketTable,
  saveMarketChoice,
  ScannerBanners,
  type SetupFilter,
  setupIntervals,
  setupOptions,
  shownOfText,
  type Sort,
  type SortColumn,
  sortRows,
  UpdatedAt,
  useNow,
} from '@/modules/market';
import {
  FilterBar,
  FilterEditor,
  filterFieldsQuery,
  filtersOfPlace,
  matchesMarketRow,
  type SavedFilter,
  savedFiltersQuery,
} from '@/modules/saved-filters';
import { groupDigits } from '@/shared/format/format';

export function MarketPage() {
  const market = useQuery(marketQuery);
  const now = useNow();
  // Kept here, not in the query: a new snapshot every 15 seconds must not reset what the owner chose.
  // What the owner chose last time, but the search; the saved filter that is gone since selects nothing.
  const [remembered] = useState(loadMarketChoice);
  const [category, setCategory] = useState<CategoryFilter>(remembered.category);
  const [search, setSearch] = useState('');
  const [setup, setSetup] = useState<SetupFilter>(remembered.setup);
  const [setupInterval, setSetupInterval] = useState(remembered.setupInterval);
  const [selectedId, setSelectedId] = useState<string | null>(remembered.savedFilterId);
  const [editing, setEditing] = useState<{ filter: SavedFilter | null } | null>(null);
  const saved = useQuery(savedFiltersQuery);
  const fields = useQuery(filterFieldsQuery);
  const ideas = useQuery(ideasQuery());
  const [sort, setSort] = useState<Sort>(remembered.sort);
  useEffect(
    () => saveMarketChoice({ category, setup, setupInterval, savedFilterId: selectedId, sort }),
    [category, setup, setupInterval, selectedId, sort],
  );
  const data = market.data;

  const sortBy = (column: SortColumn) =>
    setSort((current) => ({
      column,
      direction:
        current.column === column
          ? current.direction === 'asc'
            ? 'desc'
            : 'asc'
          : column === 'symbol'
            ? 'asc'
            : 'desc',
    }));
  const mine = filtersOfPlace(saved.data ?? [], 'market');
  const selected = mine.find((filter) => filter.id === selectedId) ?? null;
  // The chips count what the other filters leave, so a chip says how many pairs it would show.
  const beforeSetup = (data ? filterRows(data.rows, category, search) : []).filter(
    (row) => selected === null || matchesMarketRow(selected, row),
  );
  const rows = sortRows(
    filterBySetup(beforeSetup, setup, setupInterval),
    sort.column,
    sort.direction,
  );

  return (
    <section className="space-y-4">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Рынок</h1>
        <p className="text-sm text-muted">
          Все пары Bybit: linear — бессрочные USDT-контракты, spot — USDT-пары. Свечи по 1 мин,
          история за 90 дней. Только просмотр: отсюда ничего не торгуется.
        </p>
      </header>
      {market.isError && <p role="alert">Не удалось загрузить рынок.</p>}
      {market.isPending && <p className="text-sm text-muted">Загрузка рынка…</p>}
      {data && <ScannerBanners market={data} now={now} />}
      {data && data.snapshotAt === null && (
        <p className="text-sm text-muted">Сканер ещё не собрал данные.</p>
      )}
      {data?.snapshotAt && (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <div
              role="group"
              aria-label="Категория"
              className="flex gap-1 rounded-lg border border-line bg-panel p-1"
            >
              {categoryOptions(data.rows).map((option) => (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={option.value === category}
                  onClick={() => setCategory(option.value)}
                  className="h-7 rounded-md px-3 font-mono text-xs text-muted hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:font-semibold aria-pressed:text-text"
                >
                  {option.label}{' '}
                  <span className="ml-2 text-faint">{groupDigits(String(option.count))}</span>
                </button>
              ))}
            </div>
            <input
              type="search"
              aria-label="Поиск по символу"
              placeholder="⌕  Поиск по символу"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              className="h-9 w-64 rounded-lg border border-line bg-panel px-3 text-sm"
            />
            <p className="text-sm text-muted">{shownOfText(rows.length, data.rows.length)}</p>
            <div className="ml-auto">
              <UpdatedAt at={data.snapshotAt} now={now} />
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <div role="group" aria-label="Сетап" className="flex flex-wrap gap-1">
              {setupOptions(beforeSetup, setupInterval).map((option) => (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={option.value === setup}
                  onClick={() => setSetup(option.value)}
                  className="h-7 rounded-full border border-line bg-panel px-3 text-xs text-muted hover:bg-panel-2 aria-pressed:border-accent aria-pressed:text-text"
                >
                  {option.label}{' '}
                  <span className="ml-1 text-faint">{groupDigits(String(option.count))}</span>
                </button>
              ))}
            </div>
            <div role="group" aria-label="ТФ сетапа" className="ml-auto flex items-center gap-1">
              <span className="mr-1 text-xs text-faint">ТФ сетапа</span>
              {setupIntervals.map((interval) => (
                <button
                  key={interval.value}
                  type="button"
                  aria-pressed={interval.value === setupInterval}
                  onClick={() => setSetupInterval(interval.value)}
                  className="h-7 rounded-md px-2.5 text-xs font-semibold text-muted hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text"
                >
                  {interval.label}
                </button>
              ))}
            </div>
          </div>
          <FilterBar
            filters={mine}
            selected={selected}
            onSelect={setSelectedId}
            onNew={() => setEditing({ filter: null })}
            onEdit={(filter) => setEditing({ filter })}
          />
          {rows.length === 0 ? (
            <p className="text-sm text-muted">Ничего не найдено.</p>
          ) : (
            <MarketTable
              rows={rows}
              sort={sort}
              onSort={sortBy}
              dimmed={isStale(data.snapshotAt, now)}
              setupInterval={setupInterval}
            />
          )}
          <p className="text-xs text-faint">
            Изменения и оборот — по закрытым минутным свечам. OI, фандинг и спред обновляются раз в
            минуту, у spot их нет («—»). Фандинг — текущая ставка за период.
          </p>
        </>
      )}
      {editing && fields.data && data && (
        <FilterEditor
          key={editing.filter?.id ?? 'new'}
          filter={editing.filter}
          place="market"
          fields={fields.data}
          rows={data.rows}
          ideas={ideas.data ?? []}
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
