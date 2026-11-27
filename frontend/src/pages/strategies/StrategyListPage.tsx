import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';

import { api, unwrap } from '@/api/client';
import { written } from '@/api/signals';
import { listStrategies, type StrategyListItem } from '@/api/strategies';
import {
  type StrategyKind,
  strategyKindOf,
  strategyRulesOf,
} from '@/modules/graph-editor/document';
import { strategyDraftKey, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { DocumentTable, type ListType } from '@/pages/documents/DocumentTable';
import { useActiveRuns } from '@/pages/runs/useActiveRuns';
import { Badge } from '@/shared/ui/Badge';
import { Button, buttonLook } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { Page } from '@/shared/ui/Page';
import { RunStatusBadge } from '@/shared/ui/RunStatusBadge';
import { VersionBadge } from '@/shared/ui/VersionBadge';

import { kinds, wordsOf } from './kinds';
import { StrategyIdeasCell } from './StrategyIdeas';

/** The kinds of the exit nodes in the words of the list, in the order the API gives them. */
const exitWords: Record<string, string> = {
  'stop-loss': 'стоп',
  'take-profit': 'цель',
  'close-after': 'срок',
  'close-position': 'закрытие',
  'limit-order': 'лимитка',
};

/**
 * The table of the saved strategies: its words, its columns, the requests of `/api/strategies` and «Прогнать» of a
 * row, which opens the form of a run of the latest version (spec strategy-constructor «Кнопка «Прогнать»»).
 */
const strategyListOf = (navigate: ReturnType<typeof useNavigate>): ListType<StrategyListItem> => ({
  label: 'Стратегия',
  object: 'стратегию',
  queryKey: 'strategy',
  listKey: 'strategies',
  draftKey: strategyDraftKey,
  link: (name, className) => (
    <Link to="/strategies/$name" params={{ name }} className={className}>
      {name}
    </Link>
  ),
  // The signals of the latest version and its exits (spec strategy-constructor «Сигналы и выходы стратегии в списке»);
  // a signal with a newer version says so, and only the editor moves the reference («Отметка новой версии сигнала»).
  columns: [
    {
      title: 'Вид',
      className: 'w-[70px]',
      cell: (strategy) => (
        <span className="text-xs text-muted">{wordsOf(strategyKindOf(strategy.kind)).short}</span>
      ),
    },
    {
      title: 'Сигналы',
      cell: (strategy) =>
        (strategy.signals ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="flex flex-wrap items-center gap-1.5">
            {strategy.signals.map((signal) => (
              <span
                key={`${signal.name}:${signal.version}`}
                className="inline-flex items-center gap-1.5"
              >
                <Link to="/signals/$name" params={{ name: signal.name }}>
                  <Badge tone="accent" mono className="hover:brightness-125">
                    {signal.name} v{String(signal.version)}
                  </Badge>
                </Link>
                {Number(signal.latestVersion) > Number(signal.version) && (
                  <Badge tone="warn">есть v{String(signal.latestVersion)}</Badge>
                )}
              </span>
            ))}
          </span>
        ),
    },
    {
      title: 'Выход',
      className: 'w-[180px]',
      cell: (strategy) =>
        (strategy.exits ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="text-xs text-muted">
            {strategy.exits.map((exit) => exitWords[exit] ?? exit).join(' · ')}
          </span>
        ),
    },
    // Spec strategy-ideas «Идеи в списке стратегий» (change add-ideas-and-market).
    {
      title: 'Идеи',
      className: 'w-[200px]',
      cell: (strategy) => <StrategyIdeasCell strategy={strategy} />,
    },
    // Spec strategy-constructor «Последний прогон стратегии»: the state of the run with the version it went on, a link
    // to its page.
    {
      title: 'Последний прогон',
      className: 'w-[170px]',
      cell: (strategy) =>
        strategy.lastRun ? (
          <Link
            to="/runs/$id"
            params={{ id: strategy.lastRun.id }}
            aria-label={`Прогон ${strategy.lastRun.id}`}
            className="inline-flex items-center gap-1.5 hover:brightness-125"
          >
            <RunStatusBadge state={strategy.lastRun.state} />
            <VersionBadge version={strategy.lastRun.version} />
          </Link>
        ) : (
          <span className="text-xs text-faint">не запускалась</span>
        ),
    },
  ],
  menu: (strategy) => [
    {
      label: 'Прогнать',
      onSelect: () =>
        void navigate({
          to: '/strategies/$name/run',
          params: { name: strategy.name },
          search: { version: Number(strategy.version) },
        }),
    },
  ],
  rename: async (name, to) =>
    written(
      await api.POST('/api/strategies/{name}/rename', { params: { path: { name } }, body: { to } }),
    ),
  copy: async (name, to) => {
    const source = unwrap(await api.GET('/api/strategies/{name}', { params: { path: { name } } }));
    return written(
      await api.POST('/api/strategies', {
        body: {
          name: to,
          description: source.description,
          graph: source.graph,
          layout: source.layout,
          // A copy is of the kind of its source (spec strategy-constructor «Копия лесенки»).
          kind: strategyKindOf(source.kind),
        },
      }),
    );
  },
  remove: async (name) => {
    await api.DELETE('/api/strategies/{name}', { params: { path: { name } } });
    return [];
  },
  deleteQuestion: (name) => `Удалить стратегию ${name} со всеми версиями?`,
});

/**
 * Opens the editor of a new strategy of a kind: its draft holds the kind and the start graph of the kind, and the
 * strategy is not in the list before its first save.
 */
async function openNewStrategy(navigate: ReturnType<typeof useNavigate>, kind: StrategyKind) {
  const draftId = crypto.randomUUID();
  writeDraft(strategyDraftKey({ newId: draftId }), {
    description: '',
    graph: strategyRulesOf(kind).start(),
    layout: {},
    kind,
  });
  await navigate({ to: '/strategies/new/$draftId', params: { draftId } });
}

/**
 * The last run of a strategy follows the queue the header already asks for (task 12.6, spec bench-ui «Одно состояние на
 * трёх страницах»): a run that was queued, taken by a Worker or ended changes the queue, and the list is asked again.
 */
function useLastRunsFollowTheQueue(refetch: () => Promise<unknown>) {
  const active = useActiveRuns();
  const queue = active.data?.map((run) => `${run.id}:${run.state}`).join(',');
  const seen = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (queue === undefined) {
      return;
    }

    if (seen.current !== undefined && seen.current !== queue) {
      void refetch();
    }

    seen.current = queue;
  }, [queue, refetch]);
}

/**
 * The section «Стратегии» (spec strategy-constructor «Список стратегий», «Создание, переименование, копирование и
 * удаление стратегии», «Сигналы и выходы стратегии в списке», «Вид при создании стратегии»): the saved strategies in
 * the order of their name with their kinds, latest versions, signals, exits and last runs, one that does not read
 * with its error. «Новая стратегия» asks for the kind before the editor opens.
 */
export function StrategyListPage() {
  const navigate = useNavigate();
  const strategyList = useMemo(() => strategyListOf(navigate), [navigate]);
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  useLastRunsFollowTheQueue(strategies.refetch);
  const [choosing, setChoosing] = useState(false);
  const create = (
    <Button variant="primary" onClick={() => setChoosing(true)}>
      <span aria-hidden="true">+</span> Новая стратегия
    </Button>
  );
  // The empty list offers the new strategy itself, so the screen keeps one main button; until the list has come the
  // title has no button, so it does not move from the title into the empty list under the pointer.
  const titled = strategies.error !== null || (strategies.data?.length ?? 0) > 0;

  return (
    <Page>
      <DocumentTable
        type={strategyList}
        title="Стратегии"
        hint="Полный граф: сигналы, вход и правила выхода. Каждое сохранение — новая версия."
        label="Сохранённые стратегии"
        lines={strategies.data}
        pending={strategies.isPending}
        problem={strategies.error?.message ?? null}
        action={titled ? create : null}
        empty={
          <div className="flex flex-col items-center gap-3 rounded-[10px] border border-dashed border-line bg-panel px-6 py-16">
            <h2 className="text-base font-bold">Стратегий нет</h2>
            <p className="text-muted">Соберите граф с нуля или начните с готового сигнала.</p>
            <div className="mt-2 flex items-center gap-2">
              {create}
              <Link to="/signals" className={buttonLook('ghost')}>
                Выбрать сигнал
              </Link>
            </div>
          </div>
        }
      />
      {choosing && (
        <Dialog
          label="Вид новой стратегии"
          title="Вид новой стратегии"
          className="w-[460px]"
          onClose={() => setChoosing(false)}
        >
          <p className="text-muted">Вид задаётся один раз: после создания он не меняется.</p>
          <span className="flex flex-col gap-2">
            {kinds.map((words) => (
              <button
                key={words.kind}
                type="button"
                className="flex cursor-pointer flex-col items-start gap-1 rounded-md border border-line bg-panel-2 px-3 py-2.5 text-left hover:border-accent"
                onClick={() => void openNewStrategy(navigate, words.kind)}
              >
                <span className="font-semibold">{words.title}</span>
                <span className="text-xs text-muted">{words.explanation}</span>
              </button>
            ))}
          </span>
          <span className="flex justify-end">
            <Button onClick={() => setChoosing(false)}>Отмена</Button>
          </span>
        </Dialog>
      )}
    </Page>
  );
}
