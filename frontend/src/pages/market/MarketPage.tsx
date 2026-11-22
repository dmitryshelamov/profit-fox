import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { ListRefused } from '@/api/lists';
import { listMarketPairs, type MarketPair } from '@/api/market';
import { listStrategies } from '@/api/strategies';
import { FilterRow } from '@/modules/filters/FilterRow';
import { useChosenFilter } from '@/modules/filters/useChosenFilter';
import { timeframes } from '@/preview/preview';
import { Page, PageTitle } from '@/shared/ui/Page';
import { type PagedColumn, PagedTable } from '@/shared/ui/PagedTable';
import { Pager } from '@/shared/ui/Pager';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';
import { useListSettings } from '@/shared/ui/useListSettings';

import { amountText, changeText, clockText, priceWords, rateText, signTone } from './format';
import { IdeaBadges } from './IdeaBadges';

/** How often «Рынок» reads the snapshot again (design.md, decision 13). */
export const marketRefreshMs = 15_000;

/** A snapshot older than this is stale (spec market-overview «Снимок устарел»). */
export const staleMs = 3 * 60_000;

/** A rate of funding at or above this share, by its size, is lit. */
export const highRate = 0.0003;

const columns: readonly PagedColumn<MarketPair>[] = [
  {
    key: 'symbol',
    title: 'Пара',
    text: true,
    className: 'font-mono font-semibold',
    cell: (pair) => pair.symbol,
  },
  { key: 'price', title: 'Цена', numeric: true, cell: (pair) => priceWords(pair.price) },
  ...(['change1h', 'change4h', 'change24h'] as const).map((key): PagedColumn<MarketPair> => ({
    key,
    title: { change1h: '1 ч', change4h: '4 ч', change24h: '24 ч' }[key],
    numeric: true,
    cell: (pair) => <span className={signTone(pair[key])}>{changeText(pair[key])}</span>,
  })),
  {
    key: 'turnover24h',
    title: 'Оборот 24 ч',
    numeric: true,
    cell: (pair) => amountText(pair.turnover24h),
  },
  {
    key: 'openInterest',
    title: 'OI',
    numeric: true,
    cell: (pair) => <span className="text-muted">{amountText(pair.openInterestUsdt)}</span>,
  },
  {
    key: 'fundingRate',
    title: 'Фандинг',
    numeric: true,
    cell: (pair) => (
      <span
        className={Math.abs(Number(pair.fundingRate ?? 0)) >= highRate ? 'text-warn' : 'text-muted'}
      >
        {rateText(pair.fundingRate)}
      </span>
    ),
  },
  {
    key: 'ideas',
    title: 'Идеи',
    className: 'pl-10',
    cell: (pair) => <IdeaBadges ideas={pair.ideas} />,
  },
];

/**
 * «Рынок» (spec market-overview «Раздел «Рынок»»): the snapshot of the perpetual USDT pairs a page at a time, sorted by
 * any column, with the choices above the table; a row opens the page of its pair. Read again every 15 seconds.
 */
export function MarketPage({
  page,
  onPage,
}: {
  readonly page: number;
  readonly onPage: (page: number) => void;
}) {
  const navigate = useNavigate();
  const list = useListSettings('market', { field: 'turnover24h', descending: true });
  const [search, setSearch] = useState('');
  const [strategy, setStrategy] = useState('');
  const [timeframe, setTimeframe] = useState('');
  const [ideasOnly, setIdeasOnly] = useState(false);
  const choices = { search, strategy, timeframe, ideasOnly };
  const filter = useChosenFilter('market');
  const chosen = filter.chosen ?? undefined;
  const pairs = useQuery({
    queryKey: ['market', list.sort, page, list.pageSize, choices, chosen],
    queryFn: () =>
      listMarketPairs({ sort: list.sort, page, pageSize: list.pageSize, filter: chosen }, choices),
    placeholderData: keepPreviousData,
    refetchInterval: marketRefreshMs,
  });
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  // A filter deleted elsewhere is dropped by the list: it is taken off here too (spec «Фильтр удалили в другой вкладке»).
  const gone = pairs.data?.filterGone === true;
  useEffect(() => {
    if (gone) {
      filter.choose(null);
    }
  }, [gone, filter]);
  const choose =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      set(value);
      onPage(1);
    };

  return (
    <Page>
      <PageTitle title="Рынок" hint="Бессрочные USDT-пары Bybit">
        <span className="text-[12px] text-muted">
          {pairs.data?.updatedAt != null
            ? `обновлено ${clockText(pairs.data.updatedAt)}`
            : 'снимка ещё нет'}
        </span>
      </PageTitle>
      <section aria-label="Пары рынка" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <TextField
            sign="⌕"
            aria-label="Поиск по символу"
            placeholder="Поиск по символу"
            className="w-[240px]"
            value={search}
            onChange={(event) => choose(setSearch)(event.target.value)}
          />
          <SelectField
            aria-label="Стратегия идеи"
            className="w-[200px]"
            value={strategy}
            onChange={(event) => choose(setStrategy)(event.target.value)}
          >
            <option value="">Стратегия идеи: все</option>
            {(strategies.data ?? [])
              .filter((item) => item.ideas?.enabled === true)
              .map((item) => (
                <option key={item.name} value={item.name}>
                  {item.name}
                </option>
              ))}
          </SelectField>
          <SelectField
            aria-label="Таймфрейм идеи"
            className="w-[140px]"
            value={timeframe}
            onChange={(event) => choose(setTimeframe)(event.target.value)}
          >
            <option value="">ТФ идеи: все</option>
            {timeframes.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </SelectField>
          <label className="flex items-center gap-2 text-[13px] text-muted">
            <input
              type="checkbox"
              className="accent-accent"
              checked={ideasOnly}
              onChange={(event) => choose(setIdeasOnly)(event.target.checked)}
            />
            Только с идеями
          </label>
          <span className="ml-auto font-mono text-[12px] text-muted">
            {pairs.data ? `${Number(pairs.data.total)} пар` : ''}
          </span>
        </div>
        <FilterRow
          place="market"
          chosen={filter.chosen}
          onChoose={(id) => {
            filter.choose(id);
            onPage(1);
          }}
        />
        {pairs.data?.updatedAt != null && Date.now() - Number(pairs.data.updatedAt) > staleMs && (
          <p
            role="alert"
            aria-label="Снимок устарел"
            className="rounded-md border border-warn/40 bg-warn-bg px-4 py-2.5 text-warn"
          >
            Данные устарели: снимок не обновлялся с {clockText(pairs.data.updatedAt)}.
          </p>
        )}
        {pairs.error instanceof ListRefused ? (
          <p role="alert" className="text-err">
            {pairs.error.message}
          </p>
        ) : pairs.isError ? (
          <p role="alert" className="text-err">
            Стенд не отдал список пар: {pairs.error.message}
          </p>
        ) : null}
        {pairs.data && (
          <>
            <PagedTable
              label="Пары"
              columns={columns}
              rows={pairs.data.items}
              sort={list.sort}
              rowKey={(pair) => pair.symbol}
              rowLabel={(pair) => pair.symbol}
              onSort={(sort) => {
                list.setSort(sort);
                onPage(1);
              }}
              onOpen={(pair) =>
                void navigate({ to: '/market/$symbol', params: { symbol: pair.symbol } })
              }
            />
            <Pager
              total={Number(pairs.data.total)}
              page={page}
              pageSize={list.pageSize}
              onPage={onPage}
              onPageSize={(size) => {
                list.setPageSize(size);
                onPage(1);
              }}
            />
          </>
        )}
      </section>
    </Page>
  );
}
