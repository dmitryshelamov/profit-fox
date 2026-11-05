import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  type CategoryFilter,
  isStale,
  loadMarketChoice,
  marketQuery,
  MarketTable,
  saveMarketChoice,
  ScannerBanners,
  setupChoices,
  type SetupFilter,
  setupIntervals,
  shownOfText,
  type Sort,
  type SortColumn,
  UpdatedAt,
  useNow,
} from '@/modules/market';
import {
  FilterBar,
  FilterEditor,
  filterFieldsQuery,
  filtersOfPlace,
  type SavedFilter,
  savedFiltersQuery,
} from '@/modules/saved-filters';
import { groupDigits } from '@/shared/format/format';
import { useDebounced } from '@/shared/hooks/useDebounced';
import { Pager } from '@/shared/ui/Pager';

const categories: readonly { value: CategoryFilter; label: string }[] = [
  { value: 'all', label: 'Все' },
  { value: 'linear', label: 'linear' },
  { value: 'spot', label: 'spot' },
];

const setups: readonly { value: SetupFilter; label: string }[] = [
  { value: 'all', label: 'Все' },
  ...setupChoices,
];

/** The pairs of the market, one page at a time: the API filters, sorts, counts and cuts the snapshot, the address holds the number of the page. */
export function MarketPage({ page }: { page: number }) {
  const navigate = useNavigate();
  const now = useNow();
  // Kept here, not in the query: a new snapshot every 15 seconds must not reset what the owner chose.
  // What the owner chose last time, but the search; the saved filter that is gone since selects nothing.
  const [remembered] = useState(loadMarketChoice);
  const [category, setCategory] = useState<CategoryFilter>(remembered.category);
  const [search, setSearch] = useState('');
  const [setup, setSetup] = useState<SetupFilter>(remembered.setup);
  const [setupInterval, setSetupInterval] = useState(remembered.setupInterval);
  const [selectedId, setSelectedId] = useState<string | null>(remembered.savedFilterId);
  const [sort, setSort] = useState<Sort>(remembered.sort);
  const [pageSize, setPageSize] = useState(remembered.pageSize);
  const [editing, setEditing] = useState<{ filter: SavedFilter | null } | null>(null);
  const saved = useQuery(savedFiltersQuery);
  const fields = useQuery(filterFieldsQuery);
  const mine = filtersOfPlace(saved.data ?? [], 'market');
  const selected = mine.find((filter) => filter.id === selectedId) ?? null;
  // The search goes to the API after a pause in the typing; a saved filter that is remembered waits for the list of filters.
  const searched = useDebounced(search, 300);
  const market = useQuery(
    marketQuery(
      {
        category,
        search: searched,
        setup,
        setupInterval,
        savedFilterId: selected?.id ?? null,
        sort,
        page,
        pageSize,
      },
      // A search that is being typed waits: the page is already the first, and the old search is not asked for again.
      (selectedId === null || saved.isFetched) && search === searched,
    ),
  );
  const data = market.data;
  const answered = data?.page;
  const showPage = (number: number, replace = false) =>
    void navigate({ to: '/market', search: { page: number === 1 ? undefined : number }, replace });
  // Any other choice starts the table over from its first page.
  const firstPage = () => {
    if (page !== 1) {
      showPage(1, true);
    }
  };
  // The table may have become shorter: the address follows the page the API gave.
  useEffect(() => {
    if (answered !== undefined && !market.isPlaceholderData && Number(answered) !== page) {
      showPage(Number(answered), true);
    }
  }, [answered, market.isPlaceholderData, page]);
  useEffect(
    () =>
      saveMarketChoice({
        category,
        setup,
        setupInterval,
        savedFilterId: selectedId,
        sort,
        pageSize,
      }),
    [category, setup, setupInterval, selectedId, sort, pageSize],
  );

  const sortBy = (column: SortColumn) => {
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
    firstPage();
  };
  const select = (id: string | null) => {
    setSelectedId(id);
    firstPage();
  };
  // How many pairs a button of the category or of the setup stands for, from the counts the API gave.
  const categoryCount = (value: CategoryFilter) =>
    Number(value === 'all' ? data?.pairs : data?.categories[value]);
  const setupCount = (value: SetupFilter) => Number(data?.setups[value]);

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
              {categories.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={option.value === category}
                  onClick={() => {
                    setCategory(option.value);
                    firstPage();
                  }}
                  className="h-7 rounded-md px-3 font-mono text-xs text-muted hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:font-semibold aria-pressed:text-text"
                >
                  {option.label}{' '}
                  <span className="ml-2 text-faint">
                    {groupDigits(String(categoryCount(option.value)))}
                  </span>
                </button>
              ))}
            </div>
            <input
              type="search"
              aria-label="Поиск по символу"
              placeholder="⌕  Поиск по символу"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                firstPage();
              }}
              className="h-9 w-64 rounded-lg border border-line bg-panel px-3 text-sm"
            />
            <p className="text-sm text-muted">
              {shownOfText(Number(data.total), Number(data.pairs))}
            </p>
            <div className="ml-auto">
              <UpdatedAt at={data.snapshotAt} now={now} />
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <div role="group" aria-label="Сетап" className="flex flex-wrap gap-1">
              {setups.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={option.value === setup}
                  onClick={() => {
                    setSetup(option.value);
                    firstPage();
                  }}
                  className="h-7 rounded-full border border-line bg-panel px-3 text-xs text-muted hover:bg-panel-2 aria-pressed:border-accent aria-pressed:text-text"
                >
                  {option.label}{' '}
                  <span className="ml-1 text-faint">
                    {groupDigits(String(setupCount(option.value)))}
                  </span>
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
                  onClick={() => {
                    setSetupInterval(interval.value);
                    firstPage();
                  }}
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
            onSelect={select}
            onNew={() => setEditing({ filter: null })}
            onEdit={(filter) => setEditing({ filter })}
          />
          {data.items.length === 0 ? (
            <p className="text-sm text-muted">Ничего не найдено.</p>
          ) : (
            <>
              <MarketTable
                rows={data.items}
                sort={sort}
                onSort={sortBy}
                dimmed={isStale(data.snapshotAt, now)}
                setupInterval={setupInterval}
              />
              <Pager
                page={Number(data.page)}
                pageSize={Number(data.pageSize)}
                total={Number(data.total)}
                onPage={showPage}
                onPageSize={(size) => {
                  setPageSize(size);
                  firstPage();
                }}
              />
            </>
          )}
          <p className="text-xs text-faint">
            Изменения и оборот — по закрытым минутным свечам. OI, фандинг и спред обновляются раз в
            минуту, у spot их нет («—»). Фандинг — текущая ставка за период.
          </p>
        </>
      )}
      {editing && fields.data && (
        <FilterEditor
          key={editing.filter?.id ?? 'new'}
          filter={editing.filter}
          place="market"
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
