import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import {
  type CategoryFilter,
  categoryOptions,
  filterRows,
  instrumentsText,
  isStale,
  marketQuery,
  MarketTable,
  ScannerBanners,
  type Sort,
  type SortColumn,
  sortRows,
  UpdatedAt,
  useNow,
} from '@/modules/market';
import { groupDigits } from '@/shared/format/format';

export function MarketPage() {
  const market = useQuery(marketQuery);
  const now = useNow();
  // Kept here, not in the query: a new snapshot every 15 seconds must not reset what the owner chose.
  const [category, setCategory] = useState<CategoryFilter>('all');
  const [search, setSearch] = useState('');
  // As the mockup opens it: growth over the last hour first.
  const [sort, setSort] = useState<Sort>({ column: 'change1h', direction: 'desc' });
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
  const filtered = data ? filterRows(data.rows, category, search) : [];
  const rows = sortRows(filtered, sort.column, sort.direction);

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
            <p className="text-sm text-muted">{instrumentsText(rows.length)}</p>
            <div className="ml-auto">
              <UpdatedAt at={data.snapshotAt} now={now} />
            </div>
          </div>
          {rows.length === 0 ? (
            <p className="text-sm text-muted">Ничего не найдено.</p>
          ) : (
            <MarketTable
              rows={rows}
              sort={sort}
              onSort={sortBy}
              dimmed={isStale(data.snapshotAt, now)}
            />
          )}
          <p className="text-xs text-faint">
            Изменения и оборот — по закрытым минутным свечам. OI, фандинг и спред обновляются раз в
            минуту, у spot их нет («—»). Фандинг — текущая ставка за период.
          </p>
        </>
      )}
    </section>
  );
}
