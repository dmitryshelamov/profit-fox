import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import { cancelRun, listFinishedRuns, type Run } from '@/api/runs';
import { Button } from '@/shared/ui/Button';
import { Page, PageTitle, SectionTitle } from '@/shared/ui/Page';
import { RunStatusBadge, statusOfRun } from '@/shared/ui/RunStatusBadge';
import { Table, Td, Th } from '@/shared/ui/Table';

import { clockUtc, fixed, kindLabel, periodLabel, shortUtc, span } from './labels';
import { useActiveRuns } from './useActiveRuns';

/**
 * The section «Прогоны» (spec bench-run-queue «Страница «Прогоны»»): the runs that wait and go — a run that goes with
 * the bar of its progress («Ход идущего прогона полосой») — each with «Отменить», and below the finished ones, whose
 * row opens the page of the run («Строка законченного прогона»). The page asks again every 3 s while it shows a run
 * that has not ended.
 */
export function RunsPage() {
  const queries = useQueryClient();
  const active = useActiveRuns();
  const finished = useQuery({
    queryKey: ['runs', 'finished'],
    queryFn: () => listFinishedRuns(),
  });
  const cancel = useMutation({
    mutationFn: cancelRun,
    onSettled: () => queries.invalidateQueries({ queryKey: ['runs'] }),
  });

  // A run that ended leaves the active list and enters the finished one.
  const activeIds = active.data?.map((run) => run.id).join(',');
  const refetchFinished = finished.refetch;
  useEffect(() => {
    if (activeIds !== undefined) {
      void refetchFinished();
    }
  }, [activeIds, refetchFinished]);

  const going = active.data?.filter((run) => run.state === 'running').length ?? 0;
  const waiting = active.data?.filter((run) => run.state !== 'running') ?? [];

  return (
    <Page>
      <PageTitle
        title="Прогоны"
        hint="Проверки гипотез на данных стенда. Запускаются из списка гипотез."
      />

      <section aria-label="Ждут и идут" className="flex flex-col gap-4">
        <SectionTitle title="Ждут и идут" count={active.data?.length} />
        {active.isPending && <p className="text-muted">Загрузка…</p>}
        {active.error && <p className="text-err">{active.error.message}</p>}
        {active.data?.length === 0 && <p className="text-muted">Очередь пуста.</p>}
        {cancel.error && <p className="text-err">{cancel.error.message}</p>}
        {active.data && active.data.length > 0 && (
          <ul className="flex flex-col rounded-[10px] border border-line bg-panel">
            {active.data.map((run) => (
              <li
                key={run.id}
                aria-label={`Прогон ${run.hypothesis} ${run.id}`}
                className="grid min-h-[86px] grid-cols-[180px_220px_1fr_210px_auto] items-center gap-5 border-t border-line px-5 py-3.5 first:border-t-0"
              >
                <Link
                  to="/runs/$id"
                  params={{ id: run.id }}
                  className="flex items-baseline gap-2.5 font-mono font-medium hover:underline"
                >
                  <span className="text-accent">{run.hypothesis}</span>
                  <span className="text-muted">v{String(run.version)}</span>
                </Link>
                <div className="flex flex-col items-start gap-2">
                  <span>
                    {periodLabel(run.period)} · {kindLabel(run.kind)}
                  </span>
                  <RunStatusBadge status={statusOfRun(run)} />
                </div>
                {run.state === 'running' ? (
                  <Progress run={run} />
                ) : (
                  <span className="text-xs text-muted">
                    {placeWords(waiting.indexOf(run), going)}
                  </span>
                )}
                {run.state === 'running' ? (
                  <div className="flex flex-col gap-1.5">
                    <span className="font-mono">
                      {run.startedAt ? span((Date.now() - Date.parse(run.startedAt)) / 1000) : '—'}
                    </span>
                    <span className="text-xs text-muted">
                      {run.worker ?? '?'}
                      {run.startedAt ? ` · с ${clockUtc(run.startedAt)} UTC` : ''}
                    </span>
                  </div>
                ) : (
                  <span />
                )}
                <Button disabled={cancel.isPending} onClick={() => cancel.mutate(run.id)}>
                  Отменить
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-label="Законченные" className="flex flex-col gap-4">
        <SectionTitle title="Законченные" count={finished.data?.length} />
        {finished.error && <p className="text-err">{finished.error.message}</p>}
        {finished.data?.length === 0 && <p className="text-muted">Законченных прогонов нет.</p>}
        {finished.data && finished.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th className="w-[100px]">Гипотеза</Th>
                  <Th className="w-[90px]">Период</Th>
                  <Th className="w-[110px]">Вид</Th>
                  <Th className="w-[125px]">Статус</Th>
                  <Th>Вердикт</Th>
                  <Th className="w-[40px] text-right font-mono normal-case!">t</Th>
                  <Th className="w-[80px] text-right">Мощность</Th>
                  <Th className="w-[70px] text-right">Время</Th>
                  <Th className="w-[100px] text-right">Закончен</Th>
                  <Th className="w-6">
                    <span className="sr-only">Открыть</span>
                  </Th>
                </tr>
              </thead>
              <tbody>
                {finished.data.map((run) => (
                  <FinishedRow key={run.id} run={run} />
                ))}
              </tbody>
            </Table>
            <p className="-mt-1.5 text-xs text-faint">
              Строка целиком открывает отчёт прогона. Длинный текст ошибки обрезается, полный — в
              отчёте.
            </p>
          </>
        )}
      </section>
    </Page>
  );
}

/** Where a run that waits stands in the queue, in words. */
function placeWords(position: number, going: number): string {
  return position > 0
    ? `ждёт, перед ним в очереди ${position}`
    : going > 0
      ? 'ждёт, начнётся после текущего'
      : 'ждёт свободного Worker’а';
}

/**
 * The progress of a run that goes (spec bench-run-queue «Ход идущего прогона полосой»): the share of the pairs it has
 * passed as a bar and as «N из M пар · P %». While the number of pairs is not known the bar is empty and the words
 * count the pairs passed.
 */
function Progress({ run }: { readonly run: Run }) {
  const done = Number(run.pairsDone);
  const total =
    run.pairsTotal === null || run.pairsTotal === undefined ? null : Number(run.pairsTotal);
  const percent = total !== null && total > 0 ? Math.round((done / total) * 100) : null;
  return (
    <div className="flex flex-col gap-2.5">
      <div
        role="progressbar"
        aria-label="Ход прогона"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent ?? undefined}
        className="h-1.5 overflow-hidden rounded-full bg-line"
      >
        <div className="h-full rounded-full bg-run" style={{ width: `${percent ?? 0}%` }} />
      </div>
      <span className="text-xs text-muted">
        {total === null ? `${done} пар` : `${done} из ${total} пар · ${percent ?? 0} %`}
        {run.cancelRequested ? ' · отменяется' : ''}
      </span>
    </div>
  );
}

/**
 * A finished run (spec bench-run-queue «Строка законченного прогона»). Its first cell is a link to the page of the
 * run stretched over the whole row, so a click anywhere in the row and Enter on the focused row open it without a
 * handler on the row. The reason takes one line; the whole of it is the hint of the row.
 */
function FinishedRow({ run }: { readonly run: Run }) {
  const result = run.result;
  const reason = result?.reason ?? '';
  const power = result?.power === null || result?.power === undefined ? null : Number(result.power);
  return (
    <tr
      aria-label={`Законченный ${run.hypothesis} ${run.id}`}
      title={reason === '' ? undefined : reason}
      className="relative h-13 border-t border-line hover:bg-panel-2"
    >
      <Td className="font-mono font-medium">
        <Link
          to="/runs/$id"
          params={{ id: run.id }}
          className="flex items-baseline gap-2.5 outline-none after:absolute after:inset-0 after:content-[''] focus-visible:after:rounded-md focus-visible:after:outline focus-visible:after:outline-accent"
        >
          <span className="text-accent">{run.hypothesis}</span>
          <span className="text-muted">v{String(run.version)}</span>
        </Link>
      </Td>
      <Td>{periodLabel(run.period)}</Td>
      <Td className="text-muted">{kindLabel(run.kind)}</Td>
      <Td>{result ? <RunStatusBadge status={statusOfRun(run)} /> : '—'}</Td>
      <Td className="max-w-0">
        <span
          className={`block truncate ${result?.status === 'failed' ? 'text-err' : result?.status === 'insufficient_data' || result?.status === 'passed' || result?.status === 'rejected' ? 'font-mono text-xs' : ''}`}
        >
          {reason}
        </span>
      </Td>
      <Td className="text-right font-mono">{fixed(result?.t, 2)}</Td>
      <Td
        className={`text-right font-mono ${power !== null && power < 0.8 && result?.status === 'insufficient_data' ? 'text-warn' : ''}`}
      >
        {fixed(result?.power, 2)}
      </Td>
      <Td className="text-right font-mono">
        {result?.readSeconds === null || result?.readSeconds === undefined
          ? '—'
          : `${fixed(result.readSeconds, 0)} с`}
      </Td>
      <Td className="text-right font-mono text-xs text-muted">
        {result ? shortUtc(result.time) : '—'}
      </Td>
      <Td className="text-faint">
        <span aria-hidden="true">›</span>
      </Td>
    </tr>
  );
}
