import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { type FormEvent, type ReactNode, useState } from 'react';

import { api, ApiError, type RequestError, unwrap } from '@/api/client';
import { enqueueRun } from '@/api/runs';
import type { StrategyResponse } from '@/api/strategies';
import { type StrategyKind, strategyKindOf } from '@/modules/graph-editor/document';
import type { Graph } from '@/modules/graph-editor/model/types';
import { wordsOf } from '@/pages/strategies/kinds';
import { ErrorList } from '@/preview/ErrorList';
import { timeframes } from '@/preview/preview';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Page } from '@/shared/ui/Page';
import { Segmented } from '@/shared/ui/Segmented';
import { TextField } from '@/shared/ui/TextField';
import { VersionBadge } from '@/shared/ui/VersionBadge';

import { maxSets, setCount, valuesOf } from './grid';

/** The fees of a trade the bench takes when the form does not change them: those of `TradeCosts.Default`. */
export const defaultFees = { taker: 0.00055, maker: 0.0002 } as const;

/** The timeframe a new form has checked: the one the server takes without timeframes. */
const defaultTimeframe = '4h';

/** The start of the current UTC day as `2026-11-22`: the latest end of history a run takes. */
export function todayUtc(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}

/**
 * A fee as its field writes it, read as a number with a comma or a point; `null` for an empty field and `NaN` for one
 * that is not a number (task 12.2): neither goes to the server.
 */
export function feeOf(text: string): number | null {
  const trimmed = text.trim().replace(',', '.');
  return trimmed === '' ? null : Number(trimmed);
}

/** Why a fee of the form keeps the run back, or `null` when it is a number. */
function feeReason(text: string, whose: string): string | null {
  const fee = feeOf(text);
  if (fee === null) {
    return `укажите комиссию ${whose}`;
  }

  return Number.isFinite(fee) ? null : `комиссия ${whose} — не число`;
}

/** A value of a parameter of the graph as its field writes it. */
function written(value: unknown): string {
  return typeof value === 'string' ? value : JSON.stringify(value);
}

/**
 * The form of a run of a strategy, `/strategies/<name>/run` (spec bench-run-queue «Форма запуска прогона»): of the
 * version given, or of the latest one.
 */
export function RunFormPage({
  name,
  version,
}: {
  readonly name: string;
  readonly version?: number;
}) {
  const strategy = useQuery({
    queryKey: ['strategy', name, 'version', version ?? 'latest'],
    queryFn: async () => {
      const answer =
        version === undefined
          ? await api.GET('/api/strategies/{name}', { params: { path: { name } } })
          : await api.GET('/api/strategies/{name}/versions/{version}', {
              params: { path: { name, version } },
            });
      if (answer.response.status === 404) {
        throw new ApiError(
          404,
          version === undefined
            ? `Стратегии ${name} нет.`
            : `У стратегии ${name} нет версии ${version}.`,
        );
      }

      return unwrap(answer) as StrategyResponse;
    },
  });

  return (
    <Page>
      <div className="flex flex-col gap-5">
        <Link
          to="/strategies/$name"
          params={{ name }}
          className="flex items-center gap-1.5 self-start text-muted hover:text-text"
        >
          <span aria-hidden="true">←</span>
          {name}
        </Link>
        {strategy.isPending && <p className="text-muted">Загрузка…</p>}
        {strategy.error && <p className="text-err">{strategy.error.message}</p>}
        {strategy.data && <RunForm strategy={strategy.data} />}
      </div>
    </Page>
  );
}

function RunForm({ strategy }: { readonly strategy: StrategyResponse }) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const kind: StrategyKind = strategyKindOf(strategy.kind);
  const parameters = (strategy.graph as Graph).parameters;
  const [checked, setChecked] = useState<readonly string[]>([defaultTimeframe]);
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(parameters.map((parameter) => [parameter.name, written(parameter.value)])),
  );
  const [start, setStart] = useState('');
  const [end, setEnd] = useState(() => todayUtc());
  // A strategy of a bot has no «all pairs» (spec strategy-backtest «Лесенка без списка»).
  const [allPairs, setAllPairs] = useState(kind === 'idea');
  const [pairs, setPairs] = useState<readonly string[]>([]);
  const [pair, setPair] = useState('');
  const [taker, setTaker] = useState(String(defaultFees.taker));
  const [maker, setMaker] = useState(String(defaultFees.maker));
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [busy, setBusy] = useState(false);
  const instruments = useQuery({
    queryKey: ['instruments'],
    queryFn: async () => unwrap(await api.GET('/api/instruments')),
    staleTime: 60_000,
  });

  const lists = parameters.map((parameter) => valuesOf(values[parameter.name] ?? ''));
  const sets = setCount(lists, checked);
  const reason =
    checked.length === 0
      ? 'отметьте хотя бы один таймфрейм'
      : !allPairs && pairs.length === 0
        ? 'добавьте хотя бы одну пару'
        : sets > maxSets
          ? `наборов больше ${maxSets}`
          : (feeReason(taker, 'тейкера') ?? feeReason(maker, 'мейкера'));

  const addPair = () => {
    const symbol = pair.trim().toUpperCase();
    if (symbol !== '' && !pairs.includes(symbol)) {
      setPairs([...pairs, symbol]);
    }

    setPair('');
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (reason !== null || busy) {
      return;
    }

    setBusy(true);
    setErrors([]);
    try {
      const outcome = await enqueueRun({
        strategy: strategy.name,
        version: Number(strategy.version),
        values: Object.fromEntries(
          parameters
            .map((parameter, index) => [parameter.name, lists[index]!] as const)
            .filter(([, list]) => list.length > 0),
        ),
        timeframes: timeframes.filter((timeframe) => checked.includes(timeframe)),
        ...(start === '' ? {} : { start }),
        ...(end === '' ? {} : { end }),
        pairs: allPairs ? null : [...pairs],
        takerFee: feeOf(taker)!,
        makerFee: feeOf(maker)!,
      });
      if (!outcome.ok) {
        setErrors(outcome.errors);
        return;
      }

      await queries.invalidateQueries({ queryKey: ['runs'] });
      await queries.invalidateQueries({ queryKey: ['strategies'] });
      await navigate({ to: '/runs/$id', params: { id: outcome.run.id } });
    } catch (problem) {
      setErrors([
        { code: 'Failed', reason: problem instanceof Error ? problem.message : String(problem) },
      ]);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form aria-label="Запуск прогона" className="flex flex-col gap-6" onSubmit={submit}>
      <header className="flex flex-wrap items-center gap-3">
        <h1 className="text-[22px] leading-7 font-bold">
          Прогон <span className="font-mono">{strategy.name}</span>
        </h1>
        <VersionBadge version={strategy.version} />
        <span aria-label="Вид" className="inline-flex">
          <Badge tone="muted">{wordsOf(kind).title}</Badge>
        </span>
      </header>

      <div className="grid grid-cols-[180px_minmax(0,1fr)] items-start gap-x-6 gap-y-5 rounded-[10px] border border-line bg-panel px-5 py-5">
        <Row title="Таймфреймы">
          <span role="group" aria-label="Таймфреймы" className="flex items-center gap-4">
            {timeframes.map((timeframe) => (
              <label key={timeframe} className="flex items-center gap-1.5 font-mono">
                <input
                  type="checkbox"
                  className="accent-accent"
                  value={timeframe}
                  checked={checked.includes(timeframe)}
                  onChange={(event) =>
                    setChecked(
                      event.target.checked
                        ? [...checked, timeframe]
                        : checked.filter((item) => item !== timeframe),
                    )
                  }
                />
                {timeframe}
              </label>
            ))}
          </span>
        </Row>

        {parameters.length > 0 && (
          <Row title="Параметры" hint="Значения через запятую; одно — значение из графа.">
            <span className="flex flex-col gap-2">
              {parameters.map((parameter) => (
                <label key={parameter.name} className="flex items-center gap-3">
                  <span className="w-[120px] shrink-0 font-mono text-muted">{parameter.name}</span>
                  <TextField
                    aria-label={`Значения ${parameter.name}`}
                    mono
                    className="w-[320px]"
                    value={values[parameter.name] ?? ''}
                    onChange={(event) =>
                      setValues({ ...values, [parameter.name]: event.target.value })
                    }
                  />
                  <span className="text-xs text-faint">{parameter.type}</span>
                </label>
              ))}
            </span>
          </Row>
        )}

        <Row
          title="История"
          hint={
            kind === 'bot'
              ? 'Без начала — 26 недель до конца.'
              : 'Без начала — вся история стенда, с 2023-01-01.'
          }
        >
          <span className="flex items-center gap-3">
            <TextField
              aria-label="Начало истории"
              type="date"
              mono
              className="w-[160px]"
              value={start}
              onChange={(event) => setStart(event.target.value)}
            />
            <span aria-hidden="true" className="text-faint">
              —
            </span>
            <TextField
              aria-label="Конец истории"
              type="date"
              mono
              className="w-[160px]"
              value={end}
              onChange={(event) => setEnd(event.target.value)}
            />
          </span>
        </Row>

        <Row title="Пары">
          <span className="flex flex-col gap-2.5">
            {kind === 'idea' && (
              <Segmented
                label="Пары"
                mono={false}
                value={allPairs ? 'all' : 'list'}
                options={[
                  { value: 'all', label: 'все пары' },
                  { value: 'list', label: 'список' },
                ]}
                onChange={(choice) => setAllPairs(choice === 'all')}
              />
            )}
            {!allPairs && (
              <>
                <span className="flex items-center gap-2">
                  <TextField
                    aria-label="Пара"
                    sign="⌕"
                    mono
                    className="w-[200px]"
                    list="run-instruments"
                    placeholder="часть имени, BTC"
                    value={pair}
                    onChange={(event) => setPair(event.target.value.toUpperCase())}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') {
                        event.preventDefault();
                        addPair();
                      }
                    }}
                  />
                  <datalist id="run-instruments">
                    {instruments.data?.map((item) => (
                      <option key={item.id} value={item.symbol} />
                    ))}
                  </datalist>
                  <Button disabled={pair.trim() === ''} onClick={addPair}>
                    Добавить
                  </Button>
                </span>
                {pairs.length > 0 && (
                  <ul aria-label="Пары прогона" className="flex flex-wrap gap-1.5">
                    {pairs.map((symbol) => (
                      <li key={symbol}>
                        <Badge mono>
                          {symbol}
                          <button
                            type="button"
                            aria-label={`Убрать ${symbol}`}
                            className="cursor-pointer text-faint hover:text-text"
                            onClick={() => setPairs(pairs.filter((item) => item !== symbol))}
                          >
                            ✕
                          </button>
                        </Badge>
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </span>
        </Row>

        <Row title="Комиссии" hint="Доли от суммы сделки.">
          <span className="flex items-center gap-4">
            <label className="flex items-center gap-2 text-muted">
              тейкер
              <TextField
                aria-label="Комиссия тейкера"
                mono
                inputMode="decimal"
                className="w-[110px]"
                value={taker}
                onChange={(event) => setTaker(event.target.value)}
              />
            </label>
            <label className="flex items-center gap-2 text-muted">
              мейкер
              <TextField
                aria-label="Комиссия мейкера"
                mono
                inputMode="decimal"
                className="w-[110px]"
                value={maker}
                onChange={(event) => setMaker(event.target.value)}
              />
            </label>
          </span>
        </Row>
      </div>

      {errors.length > 0 && <ErrorList errors={errors} />}

      <footer className="flex items-center gap-4">
        <span
          role="status"
          aria-label="Наборов"
          className={`font-mono ${sets > maxSets ? 'text-err' : ''}`}
        >
          Наборов: {sets}
        </span>
        <Button
          type="submit"
          variant="primary"
          disabled={reason !== null || busy}
          title={reason ?? undefined}
        >
          {busy ? 'Ставлю…' : 'Поставить в очередь'}
        </Button>
        {reason !== null && <span className="text-xs text-muted">Нельзя: {reason}</span>}
      </footer>
    </form>
  );
}

/** A row of the form: its title on the left, its fields on the right with a hint under them. */
function Row({
  title,
  hint,
  children,
}: {
  readonly title: string;
  readonly hint?: string;
  readonly children: ReactNode;
}) {
  return (
    <>
      <span className="pt-1.5 font-semibold">{title}</span>
      <span className="flex flex-col gap-1.5">
        {children}
        {hint !== undefined && <span className="text-xs text-faint">{hint}</span>}
      </span>
    </>
  );
}
