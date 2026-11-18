import { useMutation, useQuery } from '@tanstack/react-query';
import { type FormEvent, useEffect, useMemo, useState } from 'react';

import { api, type Hypothesis, type Preview, type RequestError, unwrap } from '@/api/client';

import { CandleChart } from './CandleChart';
import { ErrorList } from './ErrorList';
import { EventList } from './EventList';
import { formatUtcDay, historyStart, marksOf, timeframes } from './preview';

/** What a preview request gave: the preview, or the problems that refused it. */
type Outcome = { readonly preview: Preview } | { readonly errors: readonly RequestError[] };

/**
 * The signal preview (spec signal-preview «Страница предпросмотра»): a hypothesis puts its signal and timeframe into
 * the form, the JSON and the timeframe can be changed, a pair is found by part of its name, and the preview shows the
 * candles of exploration with a mark on each event. A refused request keeps the last chart and lists its problems.
 */
export function PreviewPage() {
  const hypotheses = useQuery({
    queryKey: ['hypotheses'],
    queryFn: async () => unwrap(await api.GET('/api/hypotheses')),
  });
  const instruments = useQuery({
    queryKey: ['instruments'],
    queryFn: async () => unwrap(await api.GET('/api/instruments')),
  });

  const [hypothesisId, setHypothesisId] = useState('');
  const [signalText, setSignalText] = useState('');
  const [timeframe, setTimeframe] = useState<string>('4h');
  const [instrument, setInstrument] = useState('');
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [shown, setShown] = useState<Preview | null>(null);

  const choose = (hypothesis: Hypothesis) => {
    setHypothesisId(hypothesis.id);
    setSignalText(JSON.stringify(hypothesis.signal, null, 2));
    setTimeframe(hypothesis.timeframe ?? '4h');
  };

  const first = hypotheses.data?.find((hypothesis) => hypothesis.signal !== null);
  useEffect(() => {
    if (hypothesisId === '' && first !== undefined) {
      choose(first);
    }
  }, [first, hypothesisId]);

  const preview = useMutation({
    mutationFn: async (body: { instrument: string; timeframe: string; signal: unknown }) => {
      const answer = await api.POST('/api/preview', { body });
      if (answer.response.status === 400 && answer.error && 'errors' in answer.error) {
        return { errors: answer.error.errors } satisfies Outcome;
      }

      return { preview: unwrap(answer) } satisfies Outcome;
    },
    onSuccess: (outcome: Outcome) => {
      if ('preview' in outcome) {
        setShown(outcome.preview);
        setErrors([]);
      } else {
        setErrors(outcome.errors);
      }
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    let signal: unknown;
    try {
      signal = JSON.parse(signalText);
    } catch (problem) {
      setErrors([{ code: 'NotJson', reason: String(problem), field: 'signal' }]);
      return;
    }

    preview.mutate({ instrument: instrument.trim(), timeframe, signal });
  };

  const marks = useMemo(() => (shown ? marksOf(shown.events, shown.timeframe) : []), [shown]);
  const start = shown ? historyStart(shown.candles) : null;
  const broken = hypotheses.data?.filter((hypothesis) => hypothesis.error) ?? [];

  return (
    <main className="mx-auto flex max-w-7xl flex-col gap-4 p-4 lg:flex-row">
      <form onSubmit={submit} className="flex w-full flex-col gap-3 lg:w-96">
        <h1 className="text-lg font-semibold">Предпросмотр сигнала</h1>

        <label className="flex flex-col gap-1 text-sm">
          Гипотеза
          <select
            className="rounded bg-zinc-900 p-2"
            value={hypothesisId}
            onChange={(event) => {
              const chosen = hypotheses.data?.find((item) => item.id === event.target.value);
              if (chosen) {
                choose(chosen);
              }
            }}
          >
            <option value="" disabled>
              {hypotheses.isPending ? 'Загрузка…' : 'Выберите гипотезу'}
            </option>
            {hypotheses.data?.map((hypothesis) => (
              <option
                key={hypothesis.id}
                value={hypothesis.id}
                disabled={hypothesis.signal === null}
              >
                {hypothesis.id} {hypothesis.name}
                {hypothesis.error ? ' — ошибка в файле' : ''}
              </option>
            ))}
          </select>
        </label>
        {broken.map((hypothesis) => (
          <p key={hypothesis.id} className="text-xs text-red-300">
            {hypothesis.id}: {hypothesis.error}
          </p>
        ))}

        <label className="flex flex-col gap-1 text-sm">
          Таймфрейм
          <select
            className="rounded bg-zinc-900 p-2"
            value={timeframe}
            onChange={(event) => setTimeframe(event.target.value)}
          >
            {timeframes.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Пара
          <input
            className="rounded bg-zinc-900 p-2 font-mono"
            list="instruments"
            placeholder="часть имени, например BTC"
            value={instrument}
            onChange={(event) => setInstrument(event.target.value.toUpperCase())}
          />
          <datalist id="instruments">
            {instruments.data?.map((item) => (
              <option key={item.id} value={item.symbol} />
            ))}
          </datalist>
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Граф сигнала, JSON
          <textarea
            className="h-96 rounded bg-zinc-900 p-2 font-mono text-xs"
            spellCheck={false}
            value={signalText}
            onChange={(event) => setSignalText(event.target.value)}
          />
        </label>

        <button
          type="submit"
          className="rounded bg-amber-500 p-2 font-medium text-zinc-950 disabled:opacity-50"
          disabled={preview.isPending || instrument.trim() === ''}
        >
          {preview.isPending ? 'Считаю…' : 'Показать'}
        </button>
        {preview.error && <p className="text-sm text-red-300">{preview.error.message}</p>}
      </form>

      <section className="flex min-w-0 flex-1 flex-col gap-3">
        <p className="text-sm text-zinc-400">
          Разведка до 2026-05-15 включительно
          {shown && (
            <>
              {' · '}
              {shown.instrument} {shown.timeframe}
              {' · '}
              {start === null ? 'истории нет' : `история пары с ${formatUtcDay(start)}`}
              {' · '}
              событий {shown.events.length}
            </>
          )}
        </p>
        {errors.length > 0 && <ErrorList errors={errors} />}
        <CandleChart candles={shown?.candles ?? []} marks={marks} />
        {shown && <EventList marks={marks} candles={shown.candles} timeframe={shown.timeframe} />}
      </section>
    </main>
  );
}
