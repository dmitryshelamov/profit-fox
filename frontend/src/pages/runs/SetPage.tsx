import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { type ReactNode, useState } from 'react';

import type { RequestError } from '@/api/client';
import { enqueueRest, getRun, getRunSet, type Run, type SetNumbers } from '@/api/runs';
import { strategyKindOf } from '@/modules/graph-editor/document';
import { ErrorList } from '@/preview/ErrorList';
import { Button } from '@/shared/ui/Button';
import { Page, SectionTitle } from '@/shared/ui/Page';

import { grouped, signedPercent, signedUsdt } from './labels';
import { numberColumns } from './numbers';
import { todayUtc } from './RunFormPage';
import { RunHead } from './RunPage';
import { SortedTable } from './SortedTable';

/**
 * One set of a run, `/runs/<id>/sets/<n>` (spec bench-run-queue «Набор прогона на странице»): its values, its numbers,
 * the numbers of each pair with orders and «Прогнать на остатке истории».
 */
export function SetPage({ id, number }: { readonly id: string; readonly number: number }) {
  const run = useQuery({ queryKey: ['run', id], queryFn: () => getRun(id) });

  return (
    <Page>
      <div className="flex flex-col gap-5">
        <Link
          to="/runs/$id"
          params={{ id }}
          className="flex items-center gap-1.5 self-start text-muted hover:text-text"
        >
          <span aria-hidden="true">←</span>
          Прогон
        </Link>
        {run.isPending && <p className="text-muted">Загрузка…</p>}
        {run.error && <p className="text-err">{run.error.message}</p>}
        {run.data && (
          <>
            <RunHead run={run.data} />
            <SetResult run={run.data} number={number} />
          </>
        )}
      </div>
    </Page>
  );
}

/** Why a set has no rest of history to run on, or null when it has one. */
export function noRest(run: Run, today: string = todayUtc()): string | null {
  return run.state !== 'done'
    ? 'прогон не закончен'
    : run.settings.end >= today
      ? 'остатка истории нет'
      : null;
}

/** The result of one set: its values and numbers, the rest of history, and its pairs. */
export function SetResult({ run, number }: { readonly run: Run; readonly number: number }) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const set = useQuery({
    queryKey: ['run', run.id, 'set', number],
    queryFn: () => getRunSet(run.id, number),
    enabled: run.state === 'done',
  });
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [busy, setBusy] = useState(false);
  const idea = strategyKindOf(run.strategy.kind) === 'idea';
  const reason = noRest(run);

  const rest = async () => {
    setBusy(true);
    setErrors([]);
    try {
      const outcome = await enqueueRest(run.id, number);
      if (!outcome.ok) {
        setErrors(outcome.errors);
        return;
      }

      await queries.invalidateQueries({ queryKey: ['runs'] });
      await navigate({ to: '/runs/$id', params: { id: outcome.run.id } });
    } catch (problem) {
      setErrors([
        { code: 'Failed', reason: problem instanceof Error ? problem.message : String(problem) },
      ]);
    } finally {
      setBusy(false);
    }
  };

  const shown = set.data?.set;
  const numbers = shown?.numbers ?? null;

  return (
    <section aria-label={`Набор ${number}`} className="flex flex-col gap-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="flex flex-wrap items-center gap-3 text-base font-bold">
          Набор {number}
          {shown && (
            <span className="font-mono text-[13px] font-normal text-muted">
              {[
                shown.timeframe,
                ...shown.values.map((parameter) => `${parameter.name} = ${parameter.value}`),
              ].join(' · ')}
            </span>
          )}
        </h2>
        <span className="flex items-center gap-3">
          {reason !== null && <span className="text-xs text-muted">{reason}</span>}
          <Button
            disabled={reason !== null || busy}
            title={reason ?? undefined}
            onClick={() => void rest()}
          >
            Прогнать на остатке истории
          </Button>
        </span>
      </header>
      {errors.length > 0 && <ErrorList errors={errors} />}
      {set.isPending && run.state === 'done' && <p className="text-muted">Загрузка…</p>}
      {set.error && <p className="text-err">{set.error.message}</p>}
      {numbers && <Summary numbers={numbers} idea={idea} />}
      {set.data && (
        <>
          <SectionTitle title="Пары" count={set.data.pairs.length} />
          {set.data.pairs.length === 0 ? (
            <p className="text-muted">Ни у одной пары нет заявок.</p>
          ) : (
            <SortedTable
              label="Пары набора"
              rows={set.data.pairs}
              start={{ key: 'amount', descending: true }}
              rowLabel={(pair) => `Пара ${pair.pair}`}
              columns={[
                {
                  key: 'pair',
                  title: 'Пара',
                  className: 'font-mono',
                  value: (pair) => pair.pair,
                  cell: (pair) => pair.pair,
                },
                {
                  key: 'orders',
                  title: 'Заявок',
                  numeric: true,
                  value: (pair) => Number(pair.numbers.orders),
                  cell: (pair) => grouped(pair.numbers.orders),
                },
                ...numberColumns<(typeof set.data.pairs)[number]>((pair) => pair.numbers, {
                  pairs: false,
                  difference: idea,
                }),
                {
                  key: 'excluded',
                  title: 'Без сделки',
                  numeric: true,
                  value: (pair) => excludedOf(pair.numbers),
                  cell: (pair) => grouped(excludedOf(pair.numbers)),
                },
                {
                  key: 'skipped',
                  title: 'Пропущено',
                  numeric: true,
                  value: (pair) => Number(pair.numbers.skipped),
                  cell: (pair) => grouped(pair.numbers.skipped),
                },
              ]}
            />
          )}
        </>
      )}
    </section>
  );
}

/** The orders of a pair or a set that gave no trade, by all the reasons. */
function excludedOf(numbers: SetNumbers): number {
  const excluded = numbers.excluded;
  return (
    Number(excluded.plan) +
    Number(excluded.coverage) +
    Number(excluded.funding) +
    Number(excluded.noTrade) +
    Number(excluded.period)
  );
}

/**
 * The numbers of a set as tiles, and its orders without a trade by their reasons; the difference with a random entry is
 * of a strategy of ideas only.
 */
function Summary({ numbers, idea }: { readonly numbers: SetNumbers; readonly idea: boolean }) {
  const excluded = numbers.excluded;
  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-6 gap-3.5">
        <Tile name="Заявок">{grouped(numbers.orders)}</Tile>
        <Tile name="Сделок">{grouped(numbers.trades)}</Tile>
        <Tile name="Средний результат">{signedPercent(numbers.mean)}</Tile>
        <Tile name="Сумма, USDT">{signedUsdt(numbers.amount)}</Tile>
        {idea ? (
          <Tile name="Разница">{signedPercent(numbers.difference?.mean)}</Tile>
        ) : (
          <Tile name="Прибыльных">
            {numbers.winShare === null || numbers.winShare === undefined
              ? '—'
              : `${(Number(numbers.winShare) * 100).toFixed(0)} %`}
          </Tile>
        )}
        <Tile name="Незакрытых">
          {grouped(numbers.openCycles)}
          {Number(numbers.openCycles) > 0 && (
            <span className="ml-2 text-xs text-muted">{signedUsdt(numbers.openAmount)}</span>
          )}
        </Tile>
      </div>
      <p aria-label="Без сделки" className="text-xs text-muted">
        Без сделки: план {String(excluded.plan)} · история {String(excluded.coverage)} · фандинг{' '}
        {String(excluded.funding)} · нет сделки {String(excluded.noTrade)} · за концом истории{' '}
        {String(excluded.period)} · пропущено {String(numbers.skipped)}
      </p>
    </div>
  );
}

function Tile({ name, children }: { readonly name: string; readonly children: ReactNode }) {
  return (
    <div
      role="group"
      aria-label={name}
      className="flex min-w-0 flex-col gap-2 rounded-[10px] border border-line bg-panel px-3.5 py-3.5"
    >
      <span className="text-[11px] font-semibold tracking-wide text-muted uppercase">{name}</span>
      <span className="font-mono text-[18px] leading-7 font-semibold whitespace-nowrap">
        {children}
      </span>
    </div>
  );
}
