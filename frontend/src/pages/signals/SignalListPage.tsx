import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useRef } from 'react';

import { api, type Hypothesis, unwrap } from '@/api/client';
import { listSignals, type SignalListItem, written } from '@/api/signals';
import { draftKey } from '@/modules/graph-editor/drafts/drafts';
import { DocumentTable, type ListType } from '@/pages/documents/DocumentTable';
import { openStrategyOfSignal } from '@/pages/strategies/fromSignal';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Page, SectionTitle } from '@/shared/ui/Page';
import { RunStatusBadge, statusOfRun } from '@/shared/ui/RunStatusBadge';
import { Table, Td, Th } from '@/shared/ui/Table';
import { VersionBadge } from '@/shared/ui/VersionBadge';

import { kindLabel, periodLabel, shortUtc } from '../runs/labels';
import { useActiveRuns } from '../runs/useActiveRuns';
import { RunControls, RunNotes, useRunLaunch } from './RunControls';

/**
 * The section «Сигналы» (spec signal-constructor «Список сигналов», «Создание, переименование, копирование и
 * удаление», «Стратегии сигнала в списке»): the saved signals in the order of their name with the strategies that point
 * at each, a file that does not read with its error, and the hypotheses of the bench, whose signals open read only.
 */
export function SignalListPage() {
  const navigate = useNavigate();
  const signals = useQuery({ queryKey: ['signals'], queryFn: listSignals });
  const hypotheses = useQuery({
    queryKey: ['hypotheses'],
    queryFn: async () => unwrap(await api.GET('/api/hypotheses')),
  });

  // The last run of a hypothesis follows the queue the header already asks for: a run that was queued, taken by a
  // Worker or ended changes it, and the reasons a period is closed with it.
  const active = useActiveRuns();
  const queue = active.data?.map((run) => `${run.id}:${run.state}`).join(',');
  const seenQueue = useRef<string | undefined>(undefined);
  const refetchHypotheses = hypotheses.refetch;
  useEffect(() => {
    if (queue === undefined) {
      return;
    }

    if (seenQueue.current !== undefined && seenQueue.current !== queue) {
      void refetchHypotheses();
    }

    seenQueue.current = queue;
  }, [queue, refetchHypotheses]);

  return (
    <Page>
      <DocumentTable
        type={signalList}
        title="Сигналы"
        hint="Условие входа как часть графа. Стратегия ссылается на версию сигнала."
        label="Сохранённые сигналы"
        lines={signals.data}
        pending={signals.isPending}
        problem={signals.error?.message ?? null}
        action={
          <Button
            variant="primary"
            onClick={() =>
              navigate({ to: '/signals/new/$draftId', params: { draftId: crypto.randomUUID() } })
            }
          >
            <span aria-hidden="true">+</span> Новый сигнал
          </Button>
        }
        empty={<p className="text-muted">Сигналов нет.</p>}
      />

      <section aria-label="Гипотезы" className="flex flex-col gap-4">
        <SectionTitle
          title="Гипотезы стенда"
          hint="Сигнал гипотезы открывается только для чтения. Его можно скопировать в свой сигнал."
        />
        {hypotheses.error && <p className="text-err">{hypotheses.error.message}</p>}
        {hypotheses.data && hypotheses.data.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th className="w-[80px]">ID</Th>
                <Th>Имя</Th>
                <Th className="w-[60px]">ТФ</Th>
                <Th className="w-[80px]">Версия</Th>
                <Th>Последний прогон</Th>
                <Th className="w-px">Запуск</Th>
              </tr>
            </thead>
            {hypotheses.data.map((hypothesis) => (
              <HypothesisRows key={hypothesis.id} hypothesis={hypothesis} />
            ))}
          </Table>
        )}
      </section>
    </Page>
  );
}

/**
 * A hypothesis of the bench: its row with the last run of its version (spec bench-run-queue «Последний прогон
 * гипотезы») and the buttons of a run, and under it the line that says why a period is closed.
 */
function HypothesisRows({ hypothesis }: { readonly hypothesis: Hypothesis }) {
  const launch = useRunLaunch(hypothesis);
  const last = hypothesis.lastRun ?? null;
  const notes = hypothesis.error || launch.reasons.length > 0 || launch.refusal !== null;

  return (
    <tbody aria-label={`Гипотеза ${hypothesis.id}`} className="border-t border-line">
      <tr className="h-14">
        <Td>
          {hypothesis.signal ? (
            <Link
              to="/hypotheses/$id"
              params={{ id: hypothesis.id }}
              aria-label={`${hypothesis.id} ${hypothesis.name}`}
              className="font-mono font-medium text-accent hover:underline"
            >
              {hypothesis.id}
            </Link>
          ) : (
            <span className="font-mono text-faint">{hypothesis.id}</span>
          )}
        </Td>
        <Td className={`font-mono font-medium ${hypothesis.signal ? '' : 'text-muted'}`}>
          {hypothesis.name}
        </Td>
        <Td className="font-mono text-muted">{hypothesis.timeframe ?? '—'}</Td>
        <Td>
          <VersionBadge version={hypothesis.version} />
        </Td>
        <Td>
          {last ? (
            <Link
              to="/runs/$id"
              params={{ id: last.id }}
              className="group inline-flex items-center gap-2.5"
            >
              <RunStatusBadge status={statusOfRun(last)} />
              <span className="text-xs text-muted group-hover:text-text">
                {periodLabel(last.period)} · {kindLabel(last.kind)}
                {last.finishedAt ? ` · ${shortUtc(last.finishedAt)}` : ''}
              </span>
            </Link>
          ) : (
            <span className="text-xs text-faint">не запускалась</span>
          )}
        </Td>
        <Td>
          <RunControls hypothesis={hypothesis} launch={launch} />
        </Td>
      </tr>
      {notes && (
        <tr>
          <td />
          <td colSpan={5} className="pr-5 pb-4 pl-5">
            {hypothesis.error && <p className="mb-1.5 text-xs text-err">{hypothesis.error}</p>}
            <RunNotes hypothesis={hypothesis} launch={launch} />
          </td>
        </tr>
      )}
    </tbody>
  );
}

/** The table of the saved signals: its words, its column and the requests of `/api/signals`. */
const signalList: ListType<SignalListItem> = {
  label: 'Сигнал',
  object: 'сигнал',
  queryKey: 'signal',
  listKey: 'signals',
  draftKey,
  link: (name, className) => (
    <Link to="/signals/$name" params={{ name }} className={className}>
      {name}
    </Link>
  ),
  // The strategies any version of which points at the signal (spec signal-constructor «Стратегии сигнала в списке»).
  columns: [
    {
      title: 'Где используется',
      cell: (signal) =>
        (signal.strategies ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="flex flex-wrap gap-1.5">
            {signal.strategies.map((strategy) => (
              <Link key={strategy} to="/strategies/$name" params={{ name: strategy }}>
                <Badge tone="accent" mono className="hover:brightness-125">
                  {strategy}
                </Badge>
              </Link>
            ))}
          </span>
        ),
    },
  ],
  actions: (signal) => <CreateStrategy signal={signal} />,
  rename: async (name, to) =>
    written(
      await api.POST('/api/signals/{name}/rename', { params: { path: { name } }, body: { to } }),
    ),
  copy: async (name, to) => {
    const source = unwrap(await api.GET('/api/signals/{name}', { params: { path: { name } } }));
    return written(
      await api.POST('/api/signals', {
        body: {
          name: to,
          description: source.description,
          graph: source.graph,
          layout: source.layout,
        },
      }),
    );
  },
  // A signal a strategy points at is not deleted (spec signal-constructor «Сигнал, на который ссылается стратегия»).
  remove: async (name) => {
    const answer = await api.DELETE('/api/signals/{name}', { params: { path: { name } } });
    if (answer.response.status === 409 && answer.error && 'strategies' in answer.error) {
      return [
        {
          code: 'SignalInUse',
          reason: `Сигнал используют стратегии: ${(answer.error.strategies as string[]).join(', ')}`,
        },
      ];
    }

    return [];
  },
  deleteQuestion: (name) => `Удалить сигнал ${name}? Незакоммиченный файл не вернуть.`,
};

/** «Создать стратегию» of a saved signal that reads: a new strategy with the signal as its node. */
function CreateStrategy({ signal }: { readonly signal: SignalListItem }) {
  const navigate = useNavigate();
  const id = signal.id;
  if (!id) {
    return null;
  }

  return (
    <Button
      variant="plain"
      className="text-muted! hover:text-text!"
      onClick={() =>
        void openStrategyOfSignal(navigate, {
          id,
          name: signal.name,
          version: Number(signal.version),
        })
      }
    >
      Создать стратегию
    </Button>
  );
}
