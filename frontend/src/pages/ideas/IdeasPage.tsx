import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  getScannerState,
  type Idea,
  type IdeaChoices,
  listIdeas,
  summarizeIdeas,
} from '@/api/ideas';
import { listStrategies } from '@/api/strategies';
import { FilterRow } from '@/modules/filters/FilterRow';
import { useChosenFilter } from '@/modules/filters/useChosenFilter';
import { buttonLook } from '@/shared/ui/Button';
import { Page, PageTitle } from '@/shared/ui/Page';
import { PagedTable } from '@/shared/ui/PagedTable';
import { Pager } from '@/shared/ui/Pager';
import { Tabs } from '@/shared/ui/Tabs';
import { useListSettings } from '@/shared/ui/useListSettings';

import { IdeaChoiceRow } from './IdeaChoices';
import { closedColumns, openColumns, summaryColumns } from './ideaColumns';
import { ScannerBanner } from './ScannerBanner';

/** How often the open ideas and the state of the scanner are read again (spec «Раздел «Идеи»»: at least every 15 s). */
export const ideasRefreshMs = 15_000;

export type IdeasTab = 'open' | 'journal';

/**
 * «Идеи» (spec strategy-ideas «Раздел «Идеи»», «Состояние сканера в разделе «Идеи»»): the open ideas and the journal —
 * its summary over the closed ideas — each a page at a time with its choices; a row opens the page of its pair on the
 * timeframe of the idea with the idea chosen.
 */
export function IdeasPage({
  tab,
  page,
  onPlace,
}: {
  readonly tab: IdeasTab;
  readonly page: number;
  readonly onPlace: (tab: IdeasTab, page: number) => void;
}) {
  const navigate = useNavigate();
  const state = useQuery({
    queryKey: ['scanner-state'],
    queryFn: getScannerState,
    refetchInterval: ideasRefreshMs,
  });
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  const [openChoices, setOpenChoices] = useState<IdeaChoices>({});
  const [closedChoices, setClosedChoices] = useState<IdeaChoices>({ period: 7 });
  const filter = useChosenFilter('ideas');
  const [gone, setGone] = useState(false);
  useEffect(() => {
    if (gone) {
      filter.choose(null);
      setGone(false);
    }
  }, [gone, filter]);
  const open = Number(state.data?.open ?? 0);
  const closed = Number(state.data?.closed ?? 0);
  const enabled = (strategies.data ?? []).some((strategy) => strategy.ideas?.enabled === true);
  const nothing =
    state.data !== undefined &&
    strategies.data !== undefined &&
    !enabled &&
    open === 0 &&
    closed === 0;
  const openIdea = (idea: Idea) =>
    void navigate({
      to: '/market/$symbol',
      params: { symbol: idea.symbol },
      search: { timeframe: idea.timeframe, idea: idea.id },
    });

  return (
    <Page>
      <PageTitle
        title="Идеи"
        hint="Включённые стратегии на всех бессрочных парах. Итог идеи считается по её сроку, стопу или цели."
      />
      {state.data && <ScannerBanner state={state.data} />}
      {nothing ? (
        <div className="flex flex-col items-center gap-3 rounded-[10px] border border-dashed border-line bg-panel px-6 py-16">
          <h2 className="text-base font-bold">Идей пока нет</h2>
          <p className="text-muted">
            Идеи появятся после включения стратегии вида «категория идей».
          </p>
          <Link to="/strategies" className={buttonLook('ghost')}>
            Стратегии
          </Link>
        </div>
      ) : (
        <section aria-label="Идеи" className="flex flex-col gap-5">
          <Tabs
            label="Идеи"
            value={tab}
            className="border-b border-line"
            tabs={[
              { value: 'open', label: 'Открытые', count: open },
              { value: 'journal', label: 'Журнал', count: closed },
            ]}
            onChange={(next) => onPlace(next, 1)}
          />
          <FilterRow
            place="ideas"
            chosen={filter.chosen}
            onChoose={(id) => {
              filter.choose(id);
              onPlace(tab, 1);
            }}
          />
          {tab === 'open' ? (
            <OpenIdeas
              filter={filter.chosen}
              onGone={() => setGone(true)}
              choices={openChoices}
              page={page}
              onChoices={(choices) => {
                setOpenChoices(choices);
                onPlace('open', 1);
              }}
              onPage={(next) => onPlace('open', next)}
              onOpen={openIdea}
            />
          ) : (
            <Journal
              filter={filter.chosen}
              onGone={() => setGone(true)}
              choices={closedChoices}
              page={page}
              onChoices={(choices) => {
                setClosedChoices(choices);
                onPlace('journal', 1);
              }}
              onPage={(next) => onPlace('journal', next)}
              onOpen={openIdea}
            />
          )}
        </section>
      )}
    </Page>
  );
}

interface ListProps {
  readonly filter: string | null;
  readonly onGone: () => void;
  readonly choices: IdeaChoices;
  readonly page: number;
  readonly onChoices: (choices: IdeaChoices) => void;
  readonly onPage: (page: number) => void;
  readonly onOpen: (idea: Idea) => void;
}

function OpenIdeas({ filter, onGone, choices, page, onChoices, onPage, onOpen }: ListProps) {
  const list = useListSettings('ideas-open', { field: 'moment', descending: true });
  const ideas = useQuery({
    queryKey: ['ideas', 'open', list.sort, page, list.pageSize, choices, filter],
    queryFn: () =>
      listIdeas(
        'open',
        { sort: list.sort, page, pageSize: list.pageSize, filter: filter ?? undefined },
        choices,
      ),
    placeholderData: keepPreviousData,
    refetchInterval: ideasRefreshMs,
  });

  const gone = ideas.data?.filterGone === true;
  useEffect(() => {
    if (gone) {
      onGone();
    }
  }, [gone, onGone]);

  return (
    <>
      <IdeaChoiceRow
        kind="open"
        choices={choices}
        count={ideas.data ? `${Number(ideas.data.total)} идей` : undefined}
        onChange={onChoices}
      />
      {ideas.isError && (
        <p role="alert" className="text-err">
          {ideas.error.message}
        </p>
      )}
      {ideas.data && (
        <>
          <PagedTable
            label="Открытые идеи"
            columns={openColumns}
            rows={ideas.data.items}
            sort={list.sort}
            rowKey={(idea) => idea.id}
            rowLabel={(idea) => `${idea.symbol} ${idea.strategy} ${idea.timeframe}`}
            onSort={(sort) => {
              list.setSort(sort);
              onPage(1);
            }}
            onOpen={onOpen}
          />
          <Pager
            total={Number(ideas.data.total)}
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
    </>
  );
}

function Journal({ filter, onGone, choices, page, onChoices, onPage, onOpen }: ListProps) {
  const list = useListSettings('ideas-journal', { field: 'exitTime', descending: true });
  const summaryList = useListSettings('ideas-summary', { field: 'ideas', descending: true });
  const [summaryPage, setSummaryPage] = useState(1);
  const ideas = useQuery({
    queryKey: ['ideas', 'closed', list.sort, page, list.pageSize, choices, filter],
    queryFn: () =>
      listIdeas(
        'closed',
        { sort: list.sort, page, pageSize: list.pageSize, filter: filter ?? undefined },
        choices,
      ),
    placeholderData: keepPreviousData,
  });
  const summary = useQuery({
    queryKey: [
      'ideas',
      'summary',
      summaryList.sort,
      summaryPage,
      summaryList.pageSize,
      choices,
      filter,
    ],
    queryFn: () =>
      summarizeIdeas(
        {
          sort: summaryList.sort,
          page: summaryPage,
          pageSize: summaryList.pageSize,
          filter: filter ?? undefined,
        },
        choices,
      ),
    placeholderData: keepPreviousData,
  });
  const problem = ideas.error ?? summary.error;
  const gone = ideas.data?.filterGone === true;
  useEffect(() => {
    if (gone) {
      onGone();
    }
  }, [gone, onGone]);

  return (
    <>
      <IdeaChoiceRow
        kind="closed"
        choices={choices}
        count={ideas.data ? `${Number(ideas.data.total)} идей` : undefined}
        onChange={(next) => {
          setSummaryPage(1);
          onChoices(next);
        }}
      />
      {problem && (
        <p role="alert" className="text-err">
          {problem.message}
        </p>
      )}
      {summary.data && (
        <>
          <PagedTable
            label="Сводка журнала"
            columns={summaryColumns}
            rows={summary.data.items}
            sort={summaryList.sort}
            rowKey={(row) => `${row.strategyId}:${row.version}:${row.timeframe}`}
            rowLabel={(row) => `${row.strategy} v${String(row.version)} ${row.timeframe}`}
            onSort={(sort) => {
              summaryList.setSort(sort);
              setSummaryPage(1);
            }}
          />
          {Number(summary.data.total) > summaryList.pageSize && (
            <Pager
              total={Number(summary.data.total)}
              page={summaryPage}
              pageSize={summaryList.pageSize}
              onPage={setSummaryPage}
              onPageSize={(size) => {
                summaryList.setPageSize(size);
                setSummaryPage(1);
              }}
            />
          )}
        </>
      )}
      {ideas.data && (
        <>
          <PagedTable
            label="Закрытые идеи"
            columns={closedColumns}
            rows={ideas.data.items}
            sort={list.sort}
            rowKey={(idea) => idea.id}
            rowLabel={(idea) => `${idea.symbol} ${idea.strategy} ${idea.timeframe}`}
            onSort={(sort) => {
              list.setSort(sort);
              onPage(1);
            }}
            onOpen={onOpen}
          />
          <Pager
            total={Number(ideas.data.total)}
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
    </>
  );
}
