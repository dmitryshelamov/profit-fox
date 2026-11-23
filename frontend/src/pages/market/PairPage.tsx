import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { type IdeaChoices, listPairIdeas } from '@/api/ideas';
import {
  type ChartCandle,
  type ChartPoint,
  getMarketPair,
  getPairChart,
  getPairOpenInterest,
} from '@/api/market';
import { IdeaChoiceRow } from '@/pages/ideas/IdeaChoices';
import { pairColumns } from '@/pages/ideas/ideaColumns';
import { lengthOf, timeframes } from '@/preview/preview';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Page, SectionTitle } from '@/shared/ui/Page';
import { PagedTable } from '@/shared/ui/PagedTable';
import { Pager } from '@/shared/ui/Pager';
import { Segmented } from '@/shared/ui/Segmented';
import { pageSearch, useListSettings } from '@/shared/ui/useListSettings';
import { useNow } from '@/shared/ui/useNow';

import { amountText, changeText, clockText, priceWords, rateText, signTone } from './format';
import { marketRefreshMs } from './MarketPage';
import { PairChart } from './PairChart';
import {
  fundingOnCandles,
  ideaLines,
  ideaMarks,
  interestInUsdt,
  joinPages,
  joinPoints,
  withLive,
} from './pairSeries';
import { useLiveCandle } from './useLiveCandle';

/** The address of the page of a pair: the timeframe of the chart, the chosen idea and the page of its ideas. */
export interface PairSearch {
  readonly timeframe?: string;
  readonly idea?: string;
  readonly page?: number;
}

export function pairSearch(search: Record<string, unknown>): PairSearch {
  return {
    ...(typeof search.timeframe === 'string' &&
    (timeframes as readonly string[]).includes(search.timeframe)
      ? { timeframe: search.timeframe }
      : {}),
    ...(typeof search.idea === 'string' && search.idea !== '' ? { idea: search.idea } : {}),
    ...pageSearch(search),
  };
}

/** The address without the parts that are not set, so the first page and no idea leave nothing behind. */
export function clean(search: PairSearch): PairSearch {
  return Object.fromEntries(
    Object.entries(search).filter(
      ([key, value]) => value !== undefined && !(key === 'page' && value === 1),
    ),
  );
}

/** The time to the next payment as «01:27:55»; a dash when it is not known. */
export function untilPayment(next: number | null | undefined, now: number): string {
  if (next === null || next === undefined) {
    return '—';
  }

  const left = Math.max(0, Math.floor((next - now) / 1000));
  const parts = [Math.floor(left / 3600), Math.floor((left % 3600) / 60), left % 60];
  return parts.map((part) => String(part).padStart(2, '0')).join(':');
}

/**
 * The page of a pair (spec market-overview «Страница пары», «График пары», «Живая свеча», «Идеи на графике», «OI и
 * фандинг под графиком», «Идеи пары»): the numbers of the pair, the chart of the timeframe of the address with the live
 * candle, the marks and lines of its ideas, and the table of its ideas. A pair outside the set has no page.
 */
export function PairPage({
  symbol,
  timeframe,
  idea,
  page,
  onSearch,
}: {
  readonly symbol: string;
  readonly timeframe: string;
  readonly idea?: string;
  readonly page?: number;
  readonly onSearch: (change: PairSearch) => void;
}) {
  const pair = useQuery({
    queryKey: ['market-pair', symbol],
    queryFn: () => getMarketPair(symbol),
    refetchInterval: marketRefreshMs,
  });

  if (pair.data === null) {
    return (
      <Page>
        <div className="flex flex-col items-center gap-3 rounded-[10px] border border-dashed border-line bg-panel px-6 py-16">
          <h1 className="text-base font-bold">Пары {symbol} нет</h1>
          <p className="text-muted">Такой пары нет среди торгуемых бессрочных USDT-пар Bybit.</p>
          <Link to="/market" className="text-accent hover:underline">
            ← Рынок
          </Link>
        </div>
      </Page>
    );
  }

  return (
    <Page>
      <PairHeader symbol={symbol} pair={pair.data} problem={pair.error?.message ?? null} />
      <ChartSection
        key={`${symbol}:${timeframe}`}
        symbol={symbol}
        timeframe={timeframe}
        idea={idea ?? null}
        page={page}
        onSearch={onSearch}
      />
    </Page>
  );
}

function PairHeader({
  symbol,
  pair,
  problem,
}: {
  readonly symbol: string;
  readonly pair: Awaited<ReturnType<typeof getMarketPair>> | undefined;
  readonly problem: string | null;
}) {
  const now = useNow();
  const figure = (title: string, value: string, tone = 'text-text', extra?: string) => (
    <div className="flex flex-col gap-1">
      <span className="text-[11px] font-semibold text-muted uppercase">{title}</span>
      <span className={`font-mono text-[16px] ${tone}`} aria-label={title}>
        {value}
        {extra !== undefined && <span className="ml-2 text-[12px]">{extra}</span>}
      </span>
    </div>
  );

  return (
    <header className="flex flex-wrap items-end justify-between gap-6">
      <div className="flex flex-col gap-1">
        <Link to="/market" className="text-[12px] text-muted hover:text-text">
          ← Рынок
        </Link>
        <span className="flex items-center gap-3">
          <h1 className="font-mono text-[22px] font-bold">{symbol}</h1>
          <Badge tone="muted">бессрочный · Bybit</Badge>
        </span>
      </div>
      {problem !== null && (
        <p role="alert" className="text-err">
          {problem}
        </p>
      )}
      {pair && (
        <div className="flex flex-wrap items-end gap-10">
          <div className="flex flex-col gap-1">
            <span className="text-[11px] font-semibold text-muted uppercase">Цена</span>
            <span className="font-mono text-[16px]" aria-label="Цена">
              {priceWords(pair.price)}
              <span className={`ml-2 text-[12px] ${signTone(pair.change24h)}`}>
                {changeText(pair.change24h)} за 24 ч
              </span>
            </span>
          </div>
          {figure('Оборот 24 ч', amountText(pair.turnover24h))}
          {figure('OI', amountText(pair.openInterestUsdt))}
          {figure('Фандинг', rateText(pair.fundingRate), 'text-warn')}
          {figure(
            'До выплаты',
            untilPayment(pair.nextFunding == null ? null : Number(pair.nextFunding), now),
          )}
        </div>
      )}
    </header>
  );
}

/** The most ideas the chart draws: the newest of its window. */
const chartIdeasMost = 200;

/** How often the page asks again while the service loads the history of the pair (task 18.20). */
const historyRetryMs = 5_000;

function ChartSection({
  symbol,
  timeframe,
  idea,
  page,
  onSearch,
}: {
  readonly symbol: string;
  readonly timeframe: string;
  readonly idea: string | null;
  readonly page: number | undefined;
  readonly onSearch: (change: PairSearch) => void;
}) {
  // While the service loads the history of the pair, the page asks again every 5 seconds (task 18.20).
  const first = useQuery({
    queryKey: ['pair-chart', symbol, timeframe],
    queryFn: () => getPairChart(symbol, timeframe),
    refetchInterval: (query) => (query.state.data?.loading === true ? historyRetryMs : false),
  });
  // The open interest of the first page comes by its own request (task 19.7): the candles and the payments are drawn
  // without it, and its error leaves the chart as it is.
  const firstInterest = useQuery({
    queryKey: ['pair-chart-interest', symbol, timeframe],
    queryFn: () => getPairOpenInterest(symbol, timeframe),
    refetchInterval: (query) => (query.state.data?.loading === true ? historyRetryMs : false),
  });
  const [earlyInterestError, setEarlyInterestError] = useState<string | null>(null);
  const [earlier, setEarlier] = useState<{
    candles: ChartCandle[];
    interest: ChartPoint[];
    funding: ChartPoint[];
    done: boolean;
  }>({
    candles: [],
    interest: [],
    funding: [],
    done: false,
  });
  const loading = useRef(false);
  // A failed early page is shown and asked again by its button only; one that is still loading is asked again later.
  const [earlyError, setEarlyError] = useState<string | null>(null);
  const [earlyLoading, setEarlyLoading] = useState(false);
  const retry = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (retry.current !== null) {
        clearTimeout(retry.current);
      }
    },
    [],
  );
  // A first page read again — after a break of the stream — moved right: what the former first page had and the new one
  // does not stays with the earlier pages, so no hole opens between them (task 19.11).
  const formerFirst = useRef(first.data);
  useEffect(() => {
    const former = formerFirst.current;
    formerFirst.current = first.data;
    if (former === undefined || former === first.data || former.loading) {
      return;
    }

    setEarlier((old) => ({
      ...old,
      candles: joinPages(old.candles, former.candles),
      funding: joinPoints(old.funding, former.funding),
    }));
  }, [first.data]);
  const formerInterest = useRef(firstInterest.data);
  useEffect(() => {
    const former = formerInterest.current;
    formerInterest.current = firstInterest.data;
    if (former === undefined || former === firstInterest.data || former.loading) {
      return;
    }

    setEarlier((old) => ({ ...old, interest: joinPoints(old.interest, former.openInterest) }));
  }, [firstInterest.data]);
  const live = useLiveCandle(symbol, timeframe);
  const base = first.data?.candles;
  const candles = useMemo(() => {
    const joined = joinPages(earlier.candles, base ?? []);
    return live.closed.reduce((all, candle) => withLive(all, candle), joined);
  }, [earlier.candles, base, live.closed]);
  // A closed candle missed while the stream was broken is in the history read again: after the stream came back, and when
  // the live candle steps over a candle (task 18.24).
  const refetchCandles = first.refetch;
  const refetchInterest = firstInterest.refetch;
  const refetchHistory = useCallback(() => {
    void refetchCandles();
    void refetchInterest();
  }, [refetchCandles, refetchInterest]);
  useEffect(() => {
    if (live.reconnects > 0) {
      refetchHistory();
    }
  }, [live.reconnects, refetchHistory]);
  const newest = useRef<number | null>(null);
  const liveOpen = live.candle === null ? null : Number(live.candle.openTime);
  const lastOfHistory = base?.at(-1)?.openTime;
  useEffect(() => {
    if (liveOpen === null) {
      return;
    }

    const known = Math.max(
      newest.current ?? Number.NEGATIVE_INFINITY,
      Number(lastOfHistory ?? Number.NEGATIVE_INFINITY),
    );
    if (Number.isFinite(known) && liveOpen > known + lengthOf(timeframe)) {
      refetchHistory();
    }

    newest.current = Math.max(newest.current ?? liveOpen, liveOpen);
  }, [liveOpen, lastOfHistory, timeframe, refetchHistory]);
  const interest = useMemo(
    () =>
      interestInUsdt(joinPoints(earlier.interest, firstInterest.data?.openInterest ?? []), candles),
    [earlier.interest, firstInterest.data?.openInterest, candles],
  );
  const funding = useMemo(
    () => joinPoints(earlier.funding, first.data?.funding ?? []),
    [earlier.funding, first.data?.funding],
  );

  // The open interest of an earlier page, asked with the same end once its candles came; a failure is told, not the chart's.
  const loadEarlierInterest = (before: number) => {
    void getPairOpenInterest(symbol, timeframe, before)
      .then((answer) => {
        setEarlier((old) => ({ ...old, interest: joinPoints(answer.openInterest, old.interest) }));
      })
      .catch((error: unknown) => {
        setEarlyInterestError(error instanceof Error ? error.message : String(error));
      });
  };

  const loadEarlier = (asked = false) => {
    const oldest = candles[0];
    if (
      loading.current ||
      earlier.done ||
      (earlyError !== null && !asked) ||
      oldest === undefined ||
      (first.data?.candles.length ?? 0) < 500
    ) {
      return;
    }

    loading.current = true;
    setEarlyError(null);
    void getPairChart(symbol, timeframe, Number(oldest.openTime))
      .then((chart) => {
        if (chart.loading) {
          // Still loading: not the end of the history; asked again in 5 seconds.
          setEarlyLoading(true);
          retry.current = setTimeout(() => {
            retry.current = null;
            loadEarlierRef.current(true);
          }, historyRetryMs);
          return;
        }

        setEarlyLoading(false);
        setEarlier((old) => ({
          candles: joinPages(chart.candles, old.candles),
          interest: old.interest,
          funding: joinPoints(chart.funding, old.funding),
          done: chart.candles.length < 500,
        }));
        loadEarlierInterest(Number(oldest.openTime));
      })
      .catch((error: unknown) => {
        setEarlyLoading(false);
        setEarlyError(error instanceof Error ? error.message : String(error));
      })
      .finally(() => {
        loading.current = false;
      });
  };
  const loadEarlierRef = useRef(loadEarlier);
  loadEarlierRef.current = loadEarlier;

  const list = useListSettings('pair-ideas', { field: 'moment', descending: true });
  const [choices, setChoices] = useState<IdeaChoices>({});
  // Led to an idea without a page in the address, the table asks the page that idea is on (task 18.22).
  const around = page === undefined && idea !== null ? idea : undefined;
  const ideas = useQuery({
    queryKey: ['pair-ideas', symbol, list.sort, page ?? 1, list.pageSize, choices, around],
    queryFn: () =>
      listPairIdeas(
        symbol,
        { sort: list.sort, page: page ?? 1, pageSize: list.pageSize },
        choices,
        around !== undefined ? { idea: around } : {},
      ),
    placeholderData: keepPreviousData,
    refetchInterval: marketRefreshMs,
  });
  const shownPage = page ?? Number(ideas.data?.page ?? 1);
  // Only the answer to the request with the idea tells its page (task 19.10): the page shown before it, kept on the screen
  // while that request goes, is not that of the idea.
  const placeholder = ideas.isPlaceholderData;
  useEffect(() => {
    if (
      around !== undefined &&
      !placeholder &&
      ideas.data !== undefined &&
      Number(ideas.data.page) > 1
    ) {
      onSearch({ page: Number(ideas.data.page) });
    }
  }, [around, placeholder, ideas.data, onSearch]);
  const rows = ideas.data?.items ?? [];
  // The chart asks the ideas of its own window, without the choices of the table (task 18.22).
  const from = candles[0] === undefined ? undefined : Number(candles[0].openTime);
  const chartIdeas = useQuery({
    queryKey: ['pair-ideas', 'chart', symbol, timeframe, from],
    queryFn: () =>
      listPairIdeas(
        symbol,
        { sort: { field: 'moment', descending: true }, page: 1, pageSize: chartIdeasMost },
        {},
        { from },
      ),
    enabled: from !== undefined,
    placeholderData: keepPreviousData,
    refetchInterval: marketRefreshMs,
  });
  const drawnIdeas = useMemo(() => chartIdeas.data?.items ?? [], [chartIdeas.data]);
  const marks = useMemo(() => ideaMarks(drawnIdeas, timeframe), [drawnIdeas, timeframe]);
  const lines = useMemo(
    () =>
      ideaLines(
        drawnIdeas.filter((item) => item.state === 'open'),
        timeframe,
      ),
    [drawnIdeas, timeframe],
  );
  const nothing =
    ideas.data !== undefined &&
    Number(ideas.data.total) === 0 &&
    Object.values(choices).every((value) => !value);
  const fundingBars = useMemo(() => fundingOnCandles(funding, timeframe), [funding, timeframe]);
  const lastInterest = interest.at(-1);
  const lastPayment = funding.at(-1);

  return (
    <>
      <section aria-label="График" className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <span
            role="status"
            aria-label="Живая свеча"
            className="flex items-center gap-2 text-[12px] text-muted"
          >
            <span
              aria-hidden="true"
              className={`size-1.5 rounded-full ${live.state === 'live' ? 'bg-ok' : live.state === 'stopped' ? 'bg-err' : 'bg-faint'}`}
            />
            {live.state === 'live' && live.candle
              ? `Живая свеча · ${clockText(live.candle.updatedAt)}`
              : live.state === 'stopped'
                ? 'Обновление остановилось · подключаемся снова'
                : 'Подключаемся к живой свече…'}
          </span>
          <Segmented
            label="Таймфрейм"
            mono
            value={timeframe}
            options={timeframes.map((item) => ({ value: item, label: item }))}
            onChange={(next) => onSearch({ timeframe: next, page: 1 })}
          />
        </div>
        {first.isError && (
          <p role="alert" className="text-err">
            Стенд не отдал свечи: {first.error.message}
          </p>
        )}
        {(first.data?.loading === true || earlyLoading) && (
          <p className="text-muted">Сервис данных догружает историю пары…</p>
        )}
        {earlyError !== null && (
          <p role="alert" aria-label="Ранние свечи" className="flex items-center gap-3 text-err">
            Стенд не отдал ранние свечи: {earlyError}
            <Button variant="ghost" size="sm" onClick={() => loadEarlier(true)}>
              Повторить
            </Button>
          </p>
        )}
        <div className="overflow-hidden rounded-[10px] border border-line">
          <PairChart
            candles={candles}
            live={live.candle}
            interest={interest}
            funding={fundingBars}
            marks={marks}
            selected={idea}
            lines={lines}
            onEdge={() => loadEarlier()}
            onPick={(id) => onSearch({ idea: id, page: undefined })}
          />
        </div>
        <div className="flex flex-wrap items-center gap-8 font-mono text-[12px] text-muted">
          <span aria-label="OI под графиком">
            OI {lastInterest ? amountText(lastInterest.value) : '—'}
          </span>
          <span aria-label="Фандинг под графиком">
            Фандинг {lastPayment ? rateText(lastPayment.value) : '—'}
          </span>
          {(firstInterest.isError || earlyInterestError !== null) && (
            <span role="alert" aria-label="OI графика" className="flex items-center gap-2 text-err">
              Стенд не отдал OI: {firstInterest.error?.message ?? earlyInterestError}
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setEarlyInterestError(null);
                  void refetchInterest();
                }}
              >
                Повторить
              </Button>
            </span>
          )}
        </div>
      </section>
      <section aria-label="Идеи пары" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <SectionTitle
            title="Идеи пары"
            count={ideas.data ? Number(ideas.data.total) : undefined}
          />
          {!nothing && (
            <IdeaChoiceRow
              kind="pair"
              choices={choices}
              onChange={(next) => {
                setChoices(next);
                onSearch({ page: 1 });
              }}
            />
          )}
        </div>
        {ideas.isError && (
          <p role="alert" className="text-err">
            {ideas.error.message}
          </p>
        )}
        {nothing ? (
          <p className="rounded-[10px] border border-dashed border-line bg-panel px-6 py-10 text-center text-muted">
            Идей по этой паре ещё не было.
          </p>
        ) : (
          ideas.data && (
            <>
              <PagedTable
                label="Идеи пары"
                columns={pairColumns}
                rows={rows}
                sort={list.sort}
                selected={idea}
                rowKey={(item) => item.id}
                rowLabel={(item) =>
                  `${item.strategy} ${item.timeframe} ${item.state === 'open' ? 'открыта' : 'закрыта'} ${item.id}`
                }
                onSort={(sort) => {
                  list.setSort(sort);
                  onSearch({ page: 1 });
                }}
                onOpen={(item) => onSearch({ idea: item.id })}
              />
              <Pager
                total={Number(ideas.data.total)}
                page={shownPage}
                pageSize={list.pageSize}
                onPage={(next) => onSearch({ page: next })}
                onPageSize={(size) => {
                  list.setPageSize(size);
                  onSearch({ page: 1 });
                }}
              />
            </>
          )
        )}
      </section>
    </>
  );
}
