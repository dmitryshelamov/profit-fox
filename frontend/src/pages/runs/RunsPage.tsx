import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import { cancelRun, listFinishedRuns } from '@/api/runs';
import { buttonClass } from '@/modules/graph-editor/ui/Card';

import { fixed, kindLabel, periodLabel, runProgress, statusLabel } from './labels';
import { useActiveRuns } from './useActiveRuns';

/**
 * The section «Прогоны» (spec bench-run-queue «Страница «Прогоны»»): the runs that wait and go with their progress and
 * «Отменить», and below the finished ones. The page asks again every 3 s while it shows a run that has not ended.
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

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-6 p-6 text-sm">
      <header className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Прогоны</h1>
      </header>

      <section aria-label="Ждут и идут" className="flex flex-col gap-2">
        <h2 className="font-semibold">Ждут и идут</h2>
        {active.isPending && <p className="text-zinc-400">Загрузка…</p>}
        {active.error && <p className="text-red-300">{active.error.message}</p>}
        {active.data?.length === 0 && <p className="text-zinc-400">Очередь пуста.</p>}
        {cancel.error && <p className="text-red-300">{cancel.error.message}</p>}
        <ul className="flex flex-col divide-y divide-zinc-800 rounded border border-zinc-800">
          {active.data?.map((run) => (
            <li
              key={run.id}
              aria-label={`Прогон ${run.hypothesis} ${run.id}`}
              className="flex items-center gap-3 px-3 py-2"
            >
              <Link
                to="/runs/$id"
                params={{ id: run.id }}
                className="font-mono hover:text-amber-300"
              >
                {run.hypothesis} v{String(run.version)}
              </Link>
              <span>{periodLabel(run.period)}</span>
              <span className="text-zinc-400">{kindLabel(run.kind)}</span>
              <span className="text-zinc-400">{runProgress(run)}</span>
              <button
                type="button"
                className={`${buttonClass} ml-auto`}
                disabled={cancel.isPending}
                onClick={() => cancel.mutate(run.id)}
              >
                Отменить
              </button>
            </li>
          ))}
        </ul>
      </section>

      <section aria-label="Законченные" className="flex flex-col gap-2">
        <h2 className="font-semibold">Законченные</h2>
        {finished.error && <p className="text-red-300">{finished.error.message}</p>}
        {finished.data?.length === 0 && <p className="text-zinc-400">Законченных прогонов нет.</p>}
        {finished.data && finished.data.length > 0 && (
          <table className="w-full border-collapse text-left text-xs">
            <thead className="text-zinc-400">
              <tr>
                <th className="py-1 pr-3">Гипотеза</th>
                <th className="pr-3">Период</th>
                <th className="pr-3">Статус</th>
                <th className="pr-3">Вердикт</th>
                <th className="pr-3 text-right">t</th>
                <th className="pr-3 text-right">Мощность</th>
                <th className="text-right">Чтение, с</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800">
              {finished.data.map((run) => (
                <tr key={run.id} aria-label={`Законченный ${run.hypothesis} ${run.id}`}>
                  <td className="py-1 pr-3 font-mono">
                    <Link to="/runs/$id" params={{ id: run.id }} className="hover:text-amber-300">
                      {run.hypothesis} v{String(run.version)}
                    </Link>
                  </td>
                  <td className="pr-3">{periodLabel(run.period)}</td>
                  <td className="pr-3">{run.result ? statusLabel(run.result.status) : '—'}</td>
                  <td className="pr-3 text-zinc-400">{run.result?.reason ?? ''}</td>
                  <td className="pr-3 text-right">{fixed(run.result?.t, 2)}</td>
                  <td className="pr-3 text-right">{fixed(run.result?.power, 2)}</td>
                  <td className="text-right">{fixed(run.result?.readSeconds, 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}
