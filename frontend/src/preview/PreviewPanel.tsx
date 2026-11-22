import { useMutation, useQuery } from '@tanstack/react-query';
import {
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

import { api, type Preview, type RequestError, type StrategyPreview, unwrap } from '@/api/client';
import { canonical } from '@/modules/graph-editor/model/compare';
import type { Graph } from '@/modules/graph-editor/model/types';
import { Button } from '@/shared/ui/Button';
import { Segmented } from '@/shared/ui/Segmented';
import { Tabs } from '@/shared/ui/Tabs';
import { TextField } from '@/shared/ui/TextField';

import { CandleChart, type ChartRange } from './CandleChart';
import { ErrorList } from './ErrorList';
import { EventList } from './EventList';
import { OrderList } from './OrderList';
import {
  type EventMark,
  formatUtcDay,
  historyStart,
  keyOf,
  lengthOf,
  marksOf,
  timeframes,
} from './preview';
import { readStored, writeStored } from './stored';
import { percent, totalOf, tradeMarks, tradesOf } from './strategyPreview';
import { TradeList } from './TradeList';

/** What came back: the events of a signal, or the orders and trades of a strategy. */
type Result =
  | { readonly kind: 'signal'; readonly preview: Preview }
  | { readonly kind: 'strategy'; readonly preview: StrategyPreview };

/** A preview that came back and the graph it was asked for. */
interface Shown {
  readonly result: Result;
  readonly graph: string;
}

// The key was `profit-fox:preview-height` while the panel wrote its height on every opening; a height under this key
// is one the user chose by dragging.
const heightKey = 'profit-fox:preview-panel-height';
/** The height of the panel until the user drags it: on a screen 900 px high the canvas keeps 580 px. */
const defaultHeight = 320;
const openKey = 'profit-fox:preview-open';

type ListTab = 'trades' | 'orders';

/**
 * The preview panel at the bottom of the editor (spec signal-constructor «Панель предпросмотра», «Устаревший
 * предпросмотр»): a pair found by part of its name, a timeframe and «Показать» or Ctrl+Enter. The chart shows the
 * candles of exploration with a mark on each event, the list shows their moments in UTC. After a preview the header
 * shows its numbers: the events of a signal («Число событий в шапке панели предпросмотра»), or the orders, the trades
 * and the sum of their results of a strategy (spec strategy-constructor «Сумма результатов предпросмотра стратегии»).
 * After an edit the panel says the graph changed and keeps the old marks and numbers until the next run. The pair and
 * timeframe are remembered for each document, the height for every one.
 *
 * The selection is one state of the panel: the moment of an event or of an order. A row of a list and a mark of the
 * chart read it and change it («Метка события выбирает строку», «Метка сделки выбирает строку»): a click on a row
 * shows it on the chart, a click on a mark selects its row. A strategy lists its result on two tabs, «Сделки» and
 * «Заявки» («Вкладки предпросмотра стратегии»), and says since when the trades are computed when they are not for all
 * of exploration.
 */
export function PreviewPanel({
  graph,
  canRun,
  hasErrors = false,
  rememberAs,
  kind = 'signal',
  defaultTimeframe = '4h',
}: {
  readonly graph: Graph;
  readonly kind?: 'signal' | 'strategy';
  /** The graph has been checked and has no errors. */
  readonly canRun: boolean;
  /** The check found errors in the graph: the panel says why «Показать» is off. */
  readonly hasErrors?: boolean;
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
  const [height, setHeight] = useState(() => readStored(heightKey, defaultHeight));
  const [shown, setShown] = useState<Shown | null>(null);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [focus, setFocus] = useState<ChartRange | null>(null);
  const [selected, setSelected] = useState<number | null>(null);
  const [markClicks, setMarkClicks] = useState(0);
  const [tab, setTab] = useState<ListTab>('trades');

  useEffect(() => writeStored(choiceKey, choice), [choiceKey, choice]);
  useEffect(() => writeStored(openKey, open), [open]);

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
        setSelected(null);
        // A new result opens on its trades.
        setTab('trades');
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
  /** Selects a row and shows its moment on the chart. */
  const pick = (moment: number, timeframe: string) => {
    const span = (lengthOf(timeframe) / 1000) * 40;
    const time = (moment - lengthOf(timeframe)) / 1000;
    setSelected(moment);
    setFocus({ from: time - span, to: time + span });
  };
  /** A click on a mark of the chart selects its row; the row of a trade is on the tab «Сделки». */
  const pickMark = (mark: EventMark) => {
    setSelected(keyOf(mark));
    setMarkClicks((clicks) => clicks + 1);
    if (mark.order !== undefined) {
      setTab('trades');
    }
  };
  const start = result ? historyStart(result.candles) : null;
  const stale = shown !== null && shown.graph !== canonical(graph);
  const orders = shownResult?.kind === 'strategy' ? shownResult.preview.orders : [];
  const trades = tradesOf(orders);
  const total = totalOf(orders);

  const resize = (event: ReactPointerEvent) => {
    const top = event.clientY;
    const from = height;
    let dragged = from;
    const move = (moved: PointerEvent) => {
      dragged = Math.min(Math.max(from + top - moved.clientY, 160), 900);
      setHeight(dragged);
    };
    const stop = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', stop);
      writeStored(heightKey, dragged);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', stop);
  };

  return (
    <section
      role="region"
      aria-label="Предпросмотр"
      className="relative z-10 flex flex-col border-t border-line bg-app"
      style={open ? { height } : undefined}
    >
      {open && (
        <div
          aria-hidden="true"
          className="group absolute -top-1 right-0 left-0 flex h-2 cursor-row-resize justify-center"
          onPointerDown={resize}
        >
          <span className="mt-1.5 h-0.5 w-10 rounded-full bg-line group-hover:bg-muted" />
        </div>
      )}
      <form
        className="flex min-h-14 flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2.5 text-[13px]"
        onSubmit={(event) => {
          event.preventDefault();
          run();
        }}
      >
        <button
          type="button"
          aria-expanded={open}
          className="flex cursor-pointer items-center gap-2 font-bold hover:text-accent"
          onClick={() => setOpen((current) => !current)}
        >
          <span aria-hidden="true" className="text-[10px] text-muted">
            {open ? '▼' : '▶'}
          </span>
          Предпросмотр
        </button>
        <TextField
          aria-label="Пара"
          sign="⌕"
          mono
          className="w-[170px]"
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
        <Segmented
          label="Таймфрейм"
          value={choice.timeframe}
          options={timeframes.map((item) => ({ value: item, label: item }))}
          onChange={(timeframe) => setChoice({ ...choice, timeframe })}
        />
        <Button type="submit" variant="primary" disabled={!ready} title="Ctrl+Enter">
          {preview.isPending ? 'Считаю…' : 'Показать'}
        </Button>
        {!canRun &&
          (hasErrors ? (
            <span className="text-xs text-err">Сначала исправьте ошибку графа</span>
          ) : (
            <span className="text-xs text-faint">Граф проверяется…</span>
          ))}
        {shownResult !== undefined && !preview.isPending && (
          <p
            role="status"
            aria-label="Результат"
            className="flex items-center gap-4 border-l border-line pl-4"
          >
            {shownResult.kind === 'strategy' ? (
              <>
                <Stat name="Заявок">{orders.length}</Stat>
                <Stat name="Сделок">{trades.length}</Stat>
                {total !== null && (
                  <Stat name="Итог" tone={total < 0 ? 'err' : 'ok'}>
                    {percent(total)}
                  </Stat>
                )}
              </>
            ) : (
              <Stat name="Событий">{shownResult.preview.events.length}</Stat>
            )}
          </p>
        )}
        {stale && (
          <span role="status" className="text-xs text-warn">
            Граф изменён, нажмите Показать
          </span>
        )}
        <span className="ml-auto text-xs text-muted">
          Разведка до 2026-05-15 включительно
          {result && (
            <>
              {' · '}
              {result.instrument} {result.timeframe}
              {' · '}
              {start === null ? 'истории нет' : `история пары с ${formatUtcDay(start)}`}
            </>
          )}
        </span>
        {shownResult?.kind === 'strategy' && shownResult.preview.computedFrom != null && (
          <span className="w-full text-xs text-warn">
            Сделки посчитаны с {formatUtcDay(shownResult.preview.computedFrom)}: минутная история
            читается за последние недели с заявками, более ранние заявки не посчитаны.
          </span>
        )}
      </form>
      {open && (
        <div className="flex min-h-0 flex-1 border-t border-line">
          <div className="flex min-w-0 flex-1 flex-col gap-2 bg-canvas">
            {preview.error && <p className="px-4 pt-3 text-err">{preview.error.message}</p>}
            {errors.length > 0 && (
              <div className="px-4 pt-3">
                <ErrorList errors={errors} />
              </div>
            )}
            <div className="min-h-0 flex-1">
              <CandleChart
                candles={result?.candles ?? []}
                marks={marks}
                focus={focus}
                selected={selected}
                onPick={pickMark}
                heightClass="h-full"
              />
            </div>
          </div>
          <div
            className={`${kind === 'strategy' ? 'w-[470px]' : 'w-[360px]'} flex shrink-0 flex-col border-l border-line bg-panel`}
          >
            {shownResult?.kind === 'signal' && (
              <div className="min-h-0 flex-1 overflow-y-auto">
                <EventList
                  marks={marks}
                  candles={shownResult.preview.candles}
                  timeframe={shownResult.preview.timeframe}
                  selected={selected}
                  markClicks={markClicks}
                  onPick={(mark) => pick(mark.moment, shownResult.preview.timeframe)}
                />
              </div>
            )}
            {shownResult?.kind === 'strategy' && (
              <>
                <Tabs
                  label="Результат"
                  className="shrink-0 border-b border-line px-4"
                  value={tab}
                  tabs={[
                    { value: 'trades', label: 'Сделки', count: trades.length },
                    { value: 'orders', label: 'Заявки', count: orders.length },
                  ]}
                  onChange={setTab}
                />
                <div role="tabpanel" className="min-h-0 flex-1 overflow-y-auto">
                  {tab === 'trades' ? (
                    <TradeList
                      orders={orders}
                      selected={selected}
                      markClicks={markClicks}
                      onPick={(order) => pick(Number(order.moment), shownResult.preview.timeframe)}
                    />
                  ) : (
                    <OrderList
                      orders={orders}
                      selected={selected}
                      markClicks={markClicks}
                      onPick={(order) => pick(Number(order.moment), shownResult.preview.timeframe)}
                    />
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

/** A number of the result in the header of the panel: its name and the number, in mono; a sum is colored by its sign. */
function Stat({
  name,
  tone,
  children,
}: {
  readonly name: string;
  readonly tone?: 'ok' | 'err';
  readonly children: ReactNode;
}) {
  return (
    <span className="flex items-baseline gap-2 text-xs text-muted">
      {name}{' '}
      <span
        data-tone={tone}
        className={`font-mono text-[13px] font-semibold ${tone === 'err' ? 'text-err' : tone === 'ok' ? 'text-ok' : 'text-text'}`}
      >
        {children}
      </span>
    </span>
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
