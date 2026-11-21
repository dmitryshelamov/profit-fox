import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { type ReactNode, useState } from 'react';

import { api, unwrap } from '@/api/client';
import { getRun, type Run, type RunResult } from '@/api/runs';
import { Badge, type BadgeTone } from '@/shared/ui/Badge';
import { buttonLook } from '@/shared/ui/Button';
import { Page } from '@/shared/ui/Page';
import { RunStatusBadge, statusOfRun } from '@/shared/ui/RunStatusBadge';
import { VersionBadge } from '@/shared/ui/VersionBadge';

import {
  fixed,
  grouped,
  kindLabel,
  periodLabel,
  pollMs,
  runProgress,
  shortened,
  span,
  statusLabel,
  utc,
} from './labels';
import { Markdown } from './Markdown';

/**
 * The power a blind run needs to read the outcomes of its events (spec hypothesis-bench «Мощность вслепую»): the bench
 * refuses a verdict below it, and the tile of the power shows the power next to it.
 */
export const powerThreshold = 0.8;

/**
 * One run (spec bench-run-queue «Страница прогона», «Числа вердикта над отчётом», «Хэши прогона копируются»): the
 * status with its reason, the numbers of the line of the ledger above the report, the report, and the details of the
 * line with its hashes; a run without a report shows why: it waits, goes, was cancelled or failed.
 */
export function RunPage({ id }: { readonly id: string }) {
  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => getRun(id),
    refetchInterval: (query) =>
      query.state.data && query.state.data.state !== 'finished' ? pollMs : false,
  });

  return (
    <Page>
      <div className="flex flex-col gap-5">
        <Link
          to="/runs"
          className="flex items-center gap-1.5 self-start text-muted hover:text-text"
        >
          <span aria-hidden="true">←</span>
          Прогоны
        </Link>
        {run.isPending && <p className="text-muted">Загрузка…</p>}
        {run.error && <p className="text-err">{run.error.message}</p>}
        {run.data && <Details run={run.data} />}
      </div>
    </Page>
  );
}

const banners: Record<string, { readonly tone: BadgeTone; readonly look: string }> = {
  passed: { tone: 'ok', look: 'border-ok/40 bg-ok-bg' },
  insufficient_data: { tone: 'warn', look: 'border-warn/40 bg-warn-bg' },
  failed: { tone: 'err', look: 'border-err/40 bg-err-bg' },
  running: { tone: 'run', look: 'border-run/40 bg-run-bg' },
};
const plainBanner = { tone: 'neutral' as BadgeTone, look: 'border-line bg-panel' };

function Details({ run }: { readonly run: Run }) {
  const result = run.result;
  const status = statusOfRun(run);
  const banner = banners[status] ?? plainBanner;
  // The name of the hypothesis is in the list of hypotheses, which the lists have already read.
  const hypotheses = useQuery({
    queryKey: ['hypotheses'],
    queryFn: async () => unwrap(await api.GET('/api/hypotheses')),
    staleTime: 60_000,
  });
  const hypothesis = hypotheses.data?.find((item) => item.id === run.hypothesis);

  return (
    <>
      <header className="flex items-center justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-[22px] leading-7 font-bold">
            <span className="text-accent">{run.hypothesis}</span>
            {hypothesis ? ` ${hypothesis.name}` : ''}
          </h1>
          <VersionBadge version={run.version} />
          <Badge>{periodLabel(run.period)}</Badge>
          <Badge>{kindLabel(run.kind)}</Badge>
        </div>
        {hypothesis?.signal != null && (
          <Link
            to="/hypotheses/$id"
            params={{ id: run.hypothesis }}
            className={buttonLook('ghost')}
          >
            Открыть гипотезу
          </Link>
        )}
      </header>

      <section
        aria-label="Вердикт"
        data-tone={banner.tone}
        className={`flex flex-col gap-2 rounded-[10px] border px-5 py-4 ${banner.look}`}
      >
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <RunStatusBadge status={status} />
          {result?.reason && (
            <span className="min-w-0 font-mono text-[13px] break-words">{result.reason}</span>
          )}
        </div>
      </section>

      <div className="grid grid-cols-[minmax(0,1fr)_316px] gap-x-3.5 gap-y-5">
        {/* The tiles and the details are the line of the ledger; the report stands between them on the page. */}
        <div aria-label="Строка журнала" className="contents">
          <div className="col-span-2 grid grid-cols-6 gap-3.5">
            <Tile name="t" plain note={`нужно ${fixed(result?.requiredT, 2)}`}>
              {fixed(result?.t, 2)}
            </Tile>
            <Tile
              name="Мощность"
              note={`нужно ${powerThreshold.toFixed(2)}`}
              tone={
                result?.power !== null && result?.power !== undefined
                  ? Number(result.power) < powerThreshold
                    ? 'warn'
                    : 'ok'
                  : undefined
              }
              extra={<PowerScale power={result?.power} />}
            >
              {fixed(result?.power, 2)}
            </Tile>
            <Tile name="Блоков" note="недель с событиями">
              {grouped(result?.blocks)}
            </Tile>
            <Tile name="Событий" note="учтено">
              {grouped(result?.events)}
            </Tile>
            <Tile name="Попыток" note="строка журнала">
              {grouped(result?.attempts)}
            </Tile>
            <Tile
              name="Чтение и расчёт"
              note={
                result?.readSeconds === null || result?.readSeconds === undefined
                  ? 'время чтения и расчёта'
                  : span(Number(result.readSeconds))
              }
            >
              {result?.readSeconds === null || result?.readSeconds === undefined
                ? '—'
                : `${fixed(result.readSeconds, 0)} с`}
            </Tile>
          </div>
          {result && <Ledger run={run} result={result} />}
        </div>
        <div className={`col-start-1 row-start-2 min-w-0 ${result ? '' : 'col-span-2'}`}>
          {result?.report ? (
            <section aria-label="Отчёт">
              <Markdown text={result.report} />
            </section>
          ) : (
            <p
              role="status"
              className={`rounded-[10px] border border-line bg-panel px-5 py-4 ${result?.status === 'failed' ? 'text-err' : 'text-muted'}`}
            >
              {noReport(run)}
            </p>
          )}
        </div>
      </div>
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

/** A number of the verdict as a tile: its name, the number in mono and a note under it. */
function Tile({
  name,
  note,
  tone,
  plain = false,
  extra,
  children,
}: {
  readonly name: string;
  readonly note: string;
  readonly tone?: 'ok' | 'warn';
  /** The name is a sign of a formula, kept as written: «t», not «T». */
  readonly plain?: boolean;
  readonly extra?: ReactNode;
  readonly children: ReactNode;
}) {
  return (
    <div
      role="group"
      aria-label={name}
      className="flex min-w-0 flex-col gap-2 rounded-[10px] border border-line bg-panel px-3.5 py-3.5"
    >
      <span
        className={`text-[11px] font-semibold tracking-wide text-muted ${plain ? 'font-mono' : 'uppercase'}`}
      >
        {name}
      </span>
      <span className="flex items-center gap-3">
        <span
          className={`font-mono text-[22px] leading-7 font-semibold whitespace-nowrap ${
            tone === 'warn' ? 'text-warn' : tone === 'ok' ? 'text-ok' : ''
          }`}
        >
          {children}
        </span>
        {extra}
      </span>
      <span className="truncate text-xs text-muted">{note}</span>
    </div>
  );
}

/** The power on a scale up to the threshold: the mark at its end is the power a verdict needs. */
function PowerScale({ power }: { readonly power: number | string | null | undefined }) {
  if (power === null || power === undefined) {
    return null;
  }

  const share = Math.min(Math.max(Number(power) / powerThreshold, 0), 1);
  return (
    <span
      role="meter"
      aria-label="Мощность до порога"
      aria-valuemin={0}
      aria-valuemax={powerThreshold}
      aria-valuenow={Number(power)}
      className="relative h-1 min-w-0 flex-1 rounded-full bg-line"
    >
      <span
        className={`absolute inset-y-0 left-0 rounded-full ${share < 1 ? 'bg-warn' : 'bg-ok'}`}
        style={{ width: `${share * 100}%` }}
      />
      <span aria-hidden="true" className="absolute -top-1.5 right-0 h-4 w-0.5 rounded bg-muted" />
    </span>
  );
}

/** The details of the line of the ledger: when it ended, the versions, the hashes and the id of the run. */
function Ledger({ run, result }: { readonly run: Run; readonly result: RunResult }) {
  return (
    <section
      aria-label="Детали"
      className="col-start-2 row-start-2 self-start rounded-[10px] border border-line bg-panel"
    >
      <h2 className="px-5 pt-4 pb-3 text-base font-bold">Детали</h2>
      <dl className="text-xs">
        <Fact name="Статус">{statusLabel(result.status)}</Fact>
        <Fact name="Период">{periodLabel(run.period)}</Fact>
        <Fact name="Закончен">{utc(result.time)}</Fact>
        <Fact name="Попыток">{String(result.attempts ?? '—')}</Fact>
        <Fact name="Версия гипотезы">{String(result.version)}</Fact>
        <Fact name="Версия движка">{String(result.engineVersion)}</Fact>
        <Fact name="Хэш гипотезы" copy={result.hash}>
          {shortened(result.hash)}
        </Fact>
        <Fact name="Хэш данных" copy={result.dataHash ?? undefined}>
          {result.dataHash ? shortened(result.dataHash) : '—'}
        </Fact>
        <Fact name="Прогон" copy={run.id}>
          {shortened(run.id)}
        </Fact>
      </dl>
    </section>
  );
}

/**
 * A row of the details. A hash or an id is shown by its first and last 8 signs and copied whole by its button (spec
 * bench-run-queue «Хэши прогона копируются»).
 */
function Fact({
  name,
  copy,
  children,
}: {
  readonly name: string;
  /** The whole text the button copies; a row without it has no button. */
  readonly copy?: string;
  readonly children: ReactNode;
}) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex h-10 items-center justify-between gap-3 border-t border-line px-5">
      <dt className="shrink-0 text-muted">{name}</dt>
      <dd className="flex min-w-0 items-center gap-2 font-mono">
        <span className="truncate">{children}</span>
        {copy !== undefined && (
          <button
            type="button"
            aria-label={`Скопировать: ${name}`}
            title={copied ? 'Скопировано' : 'Скопировать'}
            className={`cursor-pointer ${copied ? 'text-ok' : 'text-faint hover:text-text'}`}
            onClick={() => {
              void navigator.clipboard.writeText(copy).then(() => {
                setCopied(true);
                setTimeout(() => setCopied(false), 1500);
              });
            }}
          >
            <span aria-hidden="true">{copied ? '✓' : '⧉'}</span>
          </button>
        )}
      </dd>
    </div>
  );
}
