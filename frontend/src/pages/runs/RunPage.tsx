import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { type ReactNode, useState } from 'react';

import { getRun, type Run, type RunSet } from '@/api/runs';
import { strategyKindOf } from '@/modules/graph-editor/document';
import { wordsOf } from '@/pages/strategies/kinds';
import { copyText } from '@/shared/copyText';
import { Badge } from '@/shared/ui/Badge';
import { Page, SectionTitle } from '@/shared/ui/Page';
import { RunStatusBadge } from '@/shared/ui/RunStatusBadge';
import { VersionBadge } from '@/shared/ui/VersionBadge';

import { fixed, historyWords, pollMs, runProgress, shortened, span, zonedMoment } from './labels';
import { markColumn, numberColumns } from './numbers';
import { SetResult } from './SetPage';
import { SortedTable } from './SortedTable';

/** The first engine version whose difference compares the pair of an order with other pairs at its moment. */
export const markedEngine = 5;

/** A run that has not ended is asked again; one that has, not. */
export const ended = (state: string) =>
  state === 'done' || state === 'failed' || state === 'cancelled';

/**
 * One run (spec bench-run-queue «Страница прогона», «Таблица наборов прогона», «Хэши прогона копируются»): its settings,
 * the version of the engine, the hash of the data, the times of reading and computing, the warning about the pairs that
 * survived and the table of its sets; a run without a result shows why: it waits, goes, was cancelled or failed. A run
 * of one set shows the result of its pairs at once, a run of the rest of history leads to its source. A run of ideas of
 * an engine version before 5 says that its difference is not comparable with that of newer runs (spec bench-run-queue
 * «Пометка разницы старой версии движка»).
 */
export function RunPage({ id }: { readonly id: string }) {
  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => getRun(id),
    // After an error, such as a run removed by a cancel in another tab, the data are of the past and asking stops
    // (task 12.5).
    refetchInterval: (query) =>
      query.state.status !== 'error' && query.state.data && !ended(query.state.data.state)
        ? pollMs
        : false,
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
        {run.data && !run.error && <Details run={run.data} />}
      </div>
    </Page>
  );
}

/** The head of a run: the strategy with its version and kind, and the state. */
export function RunHead({ run, children }: { readonly run: Run; readonly children?: ReactNode }) {
  return (
    <header className="flex flex-wrap items-center gap-3">
      <h1 className="font-mono text-[22px] leading-7 font-bold text-accent">{run.strategy.name}</h1>
      <VersionBadge version={run.strategy.version} />
      <span aria-label="Вид" className="inline-flex">
        <Badge tone="muted">{wordsOf(strategyKindOf(run.strategy.kind)).title}</Badge>
      </span>
      <RunStatusBadge state={run.state} />
      {children}
    </header>
  );
}

function Details({ run }: { readonly run: Run }) {
  const idea = strategyKindOf(run.strategy.kind) === 'idea';
  const names = run.settings.values.map((parameter) => parameter.name);
  const done = run.state === 'done';
  const old =
    idea &&
    run.engineVersion !== null &&
    run.engineVersion !== undefined &&
    Number(run.engineVersion) < markedEngine;

  return (
    <>
      <RunHead run={run}>
        {run.source && (
          <Link
            to="/runs/$id"
            params={{ id: run.source.backtest }}
            className="text-xs text-muted hover:text-text"
          >
            Остаток истории набора {String(run.source.set)} прогона{' '}
            <span className="font-mono">{shortened(run.source.backtest)}</span>
          </Link>
        )}
      </RunHead>

      {!done && (
        <p
          role="status"
          className={`rounded-[10px] border px-5 py-4 break-words ${run.state === 'failed' ? 'border-err/40 bg-err-bg text-err' : 'border-line bg-panel text-muted'}`}
        >
          {noResult(run)}
        </p>
      )}

      {run.warning !== '' && (
        <p
          aria-label="Предупреждение"
          className="rounded-[10px] border border-warn/40 bg-warn-bg px-5 py-3 text-xs text-warn"
        >
          {run.warning}
        </p>
      )}

      <div className="grid grid-cols-[minmax(0,1fr)_316px] gap-3.5">
        <section aria-label="Настройки" className="rounded-[10px] border border-line bg-panel">
          <h2 className="px-5 pt-4 pb-3 text-base font-bold">Настройки</h2>
          <dl className="text-xs">
            <Fact name="Таймфреймы">{run.settings.timeframes.join(', ')}</Fact>
            {run.settings.values.map((parameter) => (
              <Fact key={parameter.name} name={parameter.name}>
                {parameter.values.join(', ')}
              </Fact>
            ))}
            <Fact name="История">{historyWords(run)}</Fact>
            <Fact name="Пары" wrap>
              {run.settings.pairs === null || run.settings.pairs === undefined
                ? 'все пары'
                : run.settings.pairs.join(', ')}
            </Fact>
            <Fact name="Комиссии">
              тейкер {String(run.settings.takerFee)} · мейкер {String(run.settings.makerFee)}
            </Fact>
            <Fact name="Наборов">{String(run.setCount)}</Fact>
          </dl>
        </section>

        <section
          aria-label="Детали"
          className="self-start rounded-[10px] border border-line bg-panel"
        >
          <h2 className="px-5 pt-4 pb-3 text-base font-bold">Детали</h2>
          <dl className="text-xs">
            <Fact name="Поставлен">{zonedMoment(run.queuedAt)}</Fact>
            <Fact name="Закончен">{zonedMoment(run.finishedAt)}</Fact>
            <Fact name="Версия движка">
              {run.engineVersion === null || run.engineVersion === undefined
                ? '—'
                : String(run.engineVersion)}
            </Fact>
            <Fact name="Чтение">{seconds(run.readSeconds)}</Fact>
            <Fact name="Расчёт">{seconds(run.computeSeconds)}</Fact>
            <Fact name="Хэш данных" copy={run.dataHash ?? undefined}>
              {run.dataHash ? shortened(run.dataHash) : '—'}
            </Fact>
            <Fact name="Прогон" copy={run.id}>
              {shortened(run.id)}
            </Fact>
          </dl>
        </section>
      </div>

      {done && (
        <section aria-label="Наборы" className="flex flex-col gap-4">
          <SectionTitle
            title="Наборы"
            count={run.sets.length}
            hint="Строка открывает результат набора по парам."
          />
          {old && (
            <p
              role="note"
              aria-label="Разница старой версии"
              className="rounded-[10px] border border-warn/40 bg-warn-bg px-5 py-3 text-xs text-warn"
            >
              Разница этого прогона посчитана старым способом — со случайными моментами той же
              недели на той же паре — и с разницей новых прогонов не сравнима.
            </p>
          )}
          <SortedTable<RunSet>
            label="Наборы прогона"
            rows={run.sets}
            start={{ key: 'amount', descending: true }}
            rowLabel={(set) => `Набор ${String(set.number)}`}
            link={(set) => ({ id: run.id, n: String(set.number) })}
            columns={[
              {
                key: 'number',
                title: '№',
                className: 'w-[50px]',
                numeric: true,
                value: (set) => Number(set.number),
                cell: (set) => String(set.number),
              },
              ...names.map((name) => ({
                key: `value:${name}`,
                title: name,
                className: 'font-mono normal-case!',
                value: (set: RunSet) =>
                  set.values.find((parameter) => parameter.name === name)?.value ?? null,
                cell: (set: RunSet) =>
                  set.values.find((parameter) => parameter.name === name)?.value ?? '—',
              })),
              {
                key: 'timeframe',
                title: 'ТФ',
                className: 'font-mono',
                value: (set) => set.timeframe,
                cell: (set) => set.timeframe,
              },
              ...numberColumns<RunSet>((set) => set.numbers, {
                pairs: true,
                difference: idea,
                weekly: idea,
              }),
              ...(idea ? [markColumn<RunSet>((set) => set.mark)] : []),
            ]}
          />
          {run.sets.length === 1 && <SetResult run={run} number={Number(run.sets[0]!.number)} />}
        </section>
      )}
    </>
  );
}

/** A time in seconds; from a minute on with its minutes and hours too, «754 с (12 мин 34 с)» (task 12.10). */
function seconds(value: number | string | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  const whole = fixed(value, 0);
  return Math.round(Number(value)) < 60 ? `${whole} с` : `${whole} с (${span(Number(value))})`;
}

/** Why a run has no result: it waits, goes, was cancelled or failed with its whole reason. */
function noResult(run: Run): string {
  const why = run.reason ? `: ${run.reason}` : '';
  switch (run.state) {
    case 'queued':
      return 'Прогон ждёт своей очереди.';
    case 'running':
      return `Прогон идёт: ${runProgress(run)}.`;
    case 'cancelled':
      return `Прогон отменён${why}`;
    default:
      return `Прогон упал${why}`;
  }
}

/**
 * A row of the settings or of the details. A hash or an id is shown by its first and last 8 signs and copied whole by
 * its button (spec bench-run-queue «Хэши прогона копируются»).
 */
function Fact({
  name,
  copy,
  wrap = false,
  children,
}: {
  readonly name: string;
  /** The whole text the button copies; a row without it has no button. */
  readonly copy?: string;
  /** A long value goes on several lines rather than being cut. */
  readonly wrap?: boolean;
  readonly children: ReactNode;
}) {
  const [copied, setCopied] = useState<boolean | null>(null);
  return (
    <div
      className={`flex items-center justify-between gap-3 border-t border-line px-5 ${wrap ? 'min-h-10 py-2.5' : 'h-10'}`}
    >
      <dt className="shrink-0 text-muted">{name}</dt>
      <dd className="flex min-w-0 items-center gap-2 font-mono">
        <span className={wrap ? 'text-right break-words' : 'truncate'}>{children}</span>
        {copy !== undefined && (
          <button
            type="button"
            aria-label={`Скопировать: ${name}`}
            title={
              copied === null ? 'Скопировать' : copied ? 'Скопировано' : 'Не удалось скопировать'
            }
            className={`cursor-pointer ${
              copied === null ? 'text-faint hover:text-text' : copied ? 'text-ok' : 'text-err'
            }`}
            onClick={() => {
              void copyText(copy).then((done) => {
                setCopied(done);
                setTimeout(() => setCopied(null), 1500);
              });
            }}
          >
            <span aria-hidden="true">{copied === null ? '⧉' : copied ? '✓' : '✕'}</span>
          </button>
        )}
      </dd>
    </div>
  );
}
