import { useMutation, useQuery } from '@tanstack/react-query';
import {
  type PointerEvent as ReactPointerEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

import { api, type Preview, type RequestError, type StrategyPreview, unwrap } from '@/api/client';
import { canonical } from '@/modules/graph-editor/model/compare';
import type { Graph } from '@/modules/graph-editor/model/types';

import { CandleChart, type ChartRange } from './CandleChart';
import { ErrorList } from './ErrorList';
import { EventList } from './EventList';
import { OrderList } from './OrderList';
import { formatUtcDay, historyStart, lengthOf, marksOf, timeframes } from './preview';
import { readStored, writeStored } from './stored';
import { tradeMarks } from './strategyPreview';

/** What came back: the events of a signal, or the orders and trades of a strategy. */
type Result =
  | { readonly kind: 'signal'; readonly preview: Preview }
  | { readonly kind: 'strategy'; readonly preview: StrategyPreview };

/** A preview that came back and the graph it was asked for. */
interface Shown {
  readonly result: Result;
  readonly graph: string;
}

const heightKey = 'profit-fox:preview-height';
const openKey = 'profit-fox:preview-open';

/**
 * The preview panel at the bottom of the editor (spec signal-constructor «Панель предпросмотра», «Устаревший
 * предпросмотр»): a pair found by part of its name, a timeframe and «Показать» or Ctrl+Enter. The chart shows the
 * candles of exploration with a mark on each event, the list shows their moments in UTC; a click on a moment shows it
 * on the chart. After an edit the panel says the graph changed and keeps the old marks until the next run. The pair
 * and timeframe are remembered for each document, the height for every one. A strategy (spec strategy-constructor
 * «Панель предпросмотра стратегии») shows an entry and an exit mark for each trade and lists its orders with their
 * outcomes, and says since when the trades are computed when they are not for all of exploration.
 */
export function PreviewPanel({
  graph,
  canRun,
  rememberAs,
  kind = 'signal',
  defaultTimeframe = '4h',
}: {
  readonly graph: Graph;
  readonly kind?: 'signal' | 'strategy';
  /** The graph has been checked and has no errors. */
  readonly canRun: boolean;
  /** The key a signal remembers its pair and timeframe under. */
  readonly rememberAs: string;
  readonly defaultTimeframe?: string;
}) {
  const instruments = useQuery({
    queryKey: ['instruments'],
    queryFn: async () => unwrap(await api.GET('/api/instruments')),
  });
  const choiceKey = `profit-fox:preview:${rememberAs}`;
  const [choice, setChoice] = useState(() =>
    readStored(choiceKey, { instrument: '', timeframe: defaultTimeframe }),
  );
  const [open, setOpen] = useState(() => readStored(openKey, true));
  const [height, setHeight] = useState(() => readStored(heightKey, 360));
  const [shown, setShown] = useState<Shown | null>(null);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [focus, setFocus] = useState<ChartRange | null>(null);

  useEffect(() => writeStored(choiceKey, choice), [choiceKey, choice]);
  useEffect(() => writeStored(openKey, open), [open]);
  useEffect(() => writeStored(heightKey, height), [height]);

  const preview = useMutation({
    mutationFn: async (body: {
      instrument: string;
      timeframe: string;
      graph: Graph;
    }): Promise<{ errors: RequestError[]; graph: string } | { result: Result; graph: string }> => {
      const graphText = canonical(body.graph);
      if (kind === 'strategy') {
        const answer = await api.POST('/api/preview/strategy', {
          body: {
            instrument: body.instrument,
            timeframe: body.timeframe,
            strategy: body.graph as never,
          },
        });
        return (
          refusal(answer, graphText) ?? {
            result: { kind, preview: unwrap(answer) },
            graph: graphText,
          }
        );
      }

      const answer = await api.POST('/api/preview', {
        body: {
          instrument: body.instrument,
          timeframe: body.timeframe,
          signal: body.graph as never,
        },
      });
      return (
        refusal(answer, graphText) ?? {
          result: { kind, preview: unwrap(answer) },
          graph: graphText,
        }
      );
    },
    onSuccess: (outcome) => {
      if ('result' in outcome) {
        setShown({ result: outcome.result, graph: outcome.graph });
        setErrors([]);
        setFocus(null);
      } else if ('errors' in outcome) {
        setErrors(outcome.errors);
      }
    },
  });

  const ready = canRun && choice.instrument.trim() !== '' && !preview.isPending;
  const run = () => {
    if (ready) {
      preview.mutate({ instrument: choice.instrument.trim(), timeframe: choice.timeframe, graph });
    }
  };
  const runRef = useRef(run);
  runRef.current = run;

  useEffect(() => {
    const press = (event: KeyboardEvent) => {
      if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        runRef.current();
      }
    };
    window.addEventListener('keydown', press);
    return () => window.removeEventListener('keydown', press);
  }, []);

  const shownResult = shown?.result;
  const result = shownResult?.preview;
  const marks = useMemo(
    () =>
      shownResult === undefined
        ? []
        : shownResult.kind === 'strategy'
          ? tradeMarks(shownResult.preview.orders, shownResult.preview.timeframe)
          : marksOf(shownResult.preview.events, shownResult.preview.timeframe),
    [shownResult],
  );
  const pick = (moment: number, timeframe: string) => {
    const span = (lengthOf(timeframe) / 1000) * 40;
    const time = (moment - lengthOf(timeframe)) / 1000;
    setFocus({ from: time - span, to: time + span });
  };
  const start = result ? historyStart(result.candles) : null;
  const stale = shown !== null && shown.graph !== canonical(graph);

  const resize = (event: ReactPointerEvent) => {
    const top = event.clientY;
    const from = height;
    const move = (moved: PointerEvent) =>
      setHeight(Math.min(Math.max(from + top - moved.clientY, 160), 900));
    const stop = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', stop);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', stop);
  };

  return (
    <section
      role="region"
      aria-label="Предпросмотр"
      className="relative z-10 flex flex-col border-t border-zinc-800 bg-zinc-950"
      style={open ? { height } : undefined}
    >
      {open && (
        <div
          aria-hidden="true"
          className="absolute -top-1 right-0 left-0 h-2 cursor-row-resize"
          onPointerDown={resize}
        />
      )}
      <form
        className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm"
        onSubmit={(event) => {
          event.preventDefault();
          run();
        }}
      >
        <button
          type="button"
          aria-expanded={open}
          className="rounded px-1 text-zinc-400 hover:text-zinc-100"
          onClick={() => setOpen((current) => !current)}
        >
          {open ? '▾' : '▸'} Предпросмотр
        </button>
        <input
          aria-label="Пара"
          className="w-40 rounded bg-zinc-900 px-2 py-1 font-mono"
          list="preview-instruments"
          placeholder="часть имени, BTC"
          value={choice.instrument}
          onChange={(event) =>
            setChoice({ ...choice, instrument: event.target.value.toUpperCase() })
          }
        />
        <datalist id="preview-instruments">
          {instruments.data?.map((item) => (
            <option key={item.id} value={item.symbol} />
          ))}
        </datalist>
        <select
          aria-label="Таймфрейм"
          className="rounded bg-zinc-900 px-2 py-1"
          value={choice.timeframe}
          onChange={(event) => setChoice({ ...choice, timeframe: event.target.value })}
        >
          {timeframes.map((item) => (
            <option key={item} value={item}>
              {item}
            </option>
          ))}
        </select>
        <button
          type="submit"
          className="rounded bg-amber-500 px-3 py-1 font-medium text-zinc-950 disabled:opacity-50"
          disabled={!ready}
          title="Ctrl+Enter"
        >
          {preview.isPending ? 'Считаю…' : 'Показать'}
        </button>
        {!canRun && (
          <span className="text-xs text-zinc-500">Граф с ошибками или ещё проверяется.</span>
        )}
        {stale && (
          <span role="status" className="text-xs text-amber-300">
            Граф изменён, нажмите Показать
          </span>
        )}
        <span className="ml-auto text-xs text-zinc-400">
          Разведка до 2026-05-15 включительно
          {result && (
            <>
              {' · '}
              {result.instrument} {result.timeframe}
              {' · '}
              {start === null ? 'истории нет' : `история пары с ${formatUtcDay(start)}`}
              {' · '}
              {shownResult?.kind === 'strategy' ? (
                <>
                  заявок {shownResult.preview.orders.length}
                  {' · '}
                  сделок{' '}
                  {shownResult.preview.orders.filter((order) => order.outcome === 'opened').length}
                </>
              ) : (
                <>
                  событий {shownResult?.kind === 'signal' ? shownResult.preview.events.length : 0}
                </>
              )}
            </>
          )}
        </span>
        {shownResult?.kind === 'strategy' && shownResult.preview.computedFrom != null && (
          <span className="w-full text-xs text-amber-300">
            Сделки посчитаны с {formatUtcDay(shownResult.preview.computedFrom)}: минутная история
            читается за последние недели с заявками, более ранние заявки не посчитаны.
          </span>
        )}
      </form>
      {open && (
        <div className="flex min-h-0 flex-1 gap-3 px-3 pb-3">
          <div className="flex min-w-0 flex-1 flex-col gap-2">
            {preview.error && <p className="text-sm text-red-300">{preview.error.message}</p>}
            {errors.length > 0 && <ErrorList errors={errors} />}
            <div className="min-h-0 flex-1">
              <CandleChart
                candles={result?.candles ?? []}
                marks={marks}
                focus={focus}
                heightClass="h-full"
              />
            </div>
          </div>
          <div className={`${kind === 'strategy' ? 'w-96' : 'w-72'} shrink-0 overflow-y-auto`}>
            {shownResult?.kind === 'signal' && (
              <EventList
                marks={marks}
                candles={shownResult.preview.candles}
                timeframe={shownResult.preview.timeframe}
                onPick={(mark) => pick(mark.moment, shownResult.preview.timeframe)}
              />
            )}
            {shownResult?.kind === 'strategy' && (
              <OrderList
                orders={shownResult.preview.orders}
                onPick={(order) => pick(Number(order.moment), shownResult.preview.timeframe)}
              />
            )}
          </div>
        </div>
      )}
    </section>
  );
}

/** The errors of a refused preview (`400`), or null for any other answer. */
function refusal(
  answer: { error?: unknown; response: Response },
  graph: string,
): { errors: RequestError[]; graph: string } | null {
  return answer.response.status === 400 &&
    answer.error &&
    typeof answer.error === 'object' &&
    'errors' in answer.error
    ? { errors: (answer.error as { errors: RequestError[] }).errors, graph }
    : null;
}
