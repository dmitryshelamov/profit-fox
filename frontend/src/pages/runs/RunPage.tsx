import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { getRun, type Run, type RunResult } from '@/api/runs';

import { fixed, kindLabel, periodLabel, pollMs, runProgress, statusLabel, utc } from './labels';
import { Markdown } from './Markdown';
import { SectionNav } from './SectionNav';

/**
 * One run (spec bench-run-queue «Страница прогона»): the line of the ledger and the report; a run without a report shows
 * why: it waits, goes, was cancelled or failed.
 */
export function RunPage({ id }: { readonly id: string }) {
  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => getRun(id),
    refetchInterval: (query) =>
      query.state.data && query.state.data.state !== 'finished' ? pollMs : false,
  });

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-4 p-6 text-sm">
      <header className="flex items-center justify-between">
        <Link to="/runs" className="text-zinc-400 hover:text-zinc-100">
          ← Прогоны
        </Link>
        <SectionNav />
      </header>
      {run.isPending && <p className="text-zinc-400">Загрузка…</p>}
      {run.error && <p className="text-red-300">{run.error.message}</p>}
      {run.data && <Details run={run.data} />}
    </main>
  );
}

function Details({ run }: { readonly run: Run }) {
  const result = run.result;
  return (
    <>
      <h1 className="text-lg font-semibold">
        <span className="font-mono">
          {run.hypothesis} v{String(run.version)}
        </span>
        : {periodLabel(run.period)}, {kindLabel(run.kind)}
      </h1>
      {result && <Line result={result} period={run.period} />}
      {result?.report ? (
        <section aria-label="Отчёт">
          <Markdown text={result.report} />
        </section>
      ) : (
        <p role="status" className={result?.status === 'failed' ? 'text-red-300' : 'text-zinc-300'}>
          {noReport(run)}
        </p>
      )}
    </>
  );
}

/** Why a run has no report. */
function noReport(run: Run): string {
  const result = run.result;
  if (result) {
    const why = result.reason ? `: ${result.reason}` : '';
    return result.status === 'failed'
      ? `Прогон упал${why}`
      : result.status === 'cancelled'
        ? `Прогон отменён${why}`
        : 'У прогона нет отчёта.';
  }

  return run.state === 'queued'
    ? 'Прогон ждёт своей очереди.'
    : `Прогон идёт: ${runProgress(run)}.`;
}

/** The line of the ledger of the run. */
function Line({ result, period }: { readonly result: RunResult; readonly period: string }) {
  const fields: [string, string][] = [
    ['Статус', statusLabel(result.status)],
    ['Причина', result.reason ?? '—'],
    ['Версия гипотезы', String(result.version)],
    ['Версия движка', String(result.engineVersion)],
    ['Период', periodLabel(period)],
    ['Хэш данных', result.dataHash ?? '—'],
    ['Попыток', String(result.attempts ?? '—')],
    ['Нужный t', fixed(result.requiredT, 2)],
    ['t', fixed(result.t, 2)],
    ['Мощность', fixed(result.power, 2)],
    ['Блоков', String(result.blocks ?? '—')],
    ['Событий', String(result.events ?? '—')],
    ['Чтение и расчёт, с', fixed(result.readSeconds, 0)],
    ['Закончен', utc(result.time)],
  ];
  return (
    <dl
      aria-label="Строка журнала"
      className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-xs"
    >
      {fields.map(([name, value]) => (
        <div key={name} className="contents">
          <dt className="text-zinc-400">{name}</dt>
          <dd className="font-mono break-all">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
