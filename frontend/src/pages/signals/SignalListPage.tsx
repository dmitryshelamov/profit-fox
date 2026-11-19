import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { api, type RequestError, unwrap } from '@/api/client';
import { listSignals, type SignalListItem, written } from '@/api/signals';
import { draftKey, moveDraft, removeDraft } from '@/modules/graph-editor/drafts/drafts';
import { buttonClass, fieldClass } from '@/modules/graph-editor/ui/Card';

/**
 * The section «Сигналы» (spec signal-constructor «Список сигналов», «Создание, переименование, копирование и
 * удаление»): the saved signals in the order of their name, a file that does not read with its error, and the
 * hypotheses of the bench, whose signals open read only.
 */
export function SignalListPage() {
  const navigate = useNavigate();
  const signals = useQuery({ queryKey: ['signals'], queryFn: listSignals });
  const hypotheses = useQuery({
    queryKey: ['hypotheses'],
    queryFn: async () => unwrap(await api.GET('/api/hypotheses')),
  });

  return (
    <main className="mx-auto flex max-w-4xl flex-col gap-6 p-6 text-sm">
      <header className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Сигналы</h1>
        <button
          type="button"
          className={`${buttonClass} border-amber-500 px-3 py-1.5 text-sm`}
          onClick={() =>
            navigate({ to: '/signals/new/$draftId', params: { draftId: crypto.randomUUID() } })
          }
        >
          Новый сигнал
        </button>
      </header>

      <section aria-label="Сохранённые сигналы" className="flex flex-col gap-2">
        {signals.isPending && <p className="text-zinc-400">Загрузка…</p>}
        {signals.error && <p className="text-red-300">{signals.error.message}</p>}
        {signals.data?.length === 0 && (
          <p className="text-zinc-400">Сигналов нет. Сохранённые сигналы лежат в bench/signals/.</p>
        )}
        <ul className="flex flex-col divide-y divide-zinc-800 rounded border border-zinc-800">
          {signals.data?.map((signal) => (
            <SignalRow key={signal.name} signal={signal} />
          ))}
        </ul>
      </section>

      <section aria-label="Гипотезы" className="flex flex-col gap-2">
        <h2 className="font-semibold">Гипотезы стенда</h2>
        <p className="text-xs text-zinc-400">
          Сигнал гипотезы открывается только для чтения; его можно скопировать в новый сигнал.
        </p>
        {hypotheses.error && <p className="text-red-300">{hypotheses.error.message}</p>}
        <ul className="flex flex-col divide-y divide-zinc-800 rounded border border-zinc-800">
          {hypotheses.data?.map((hypothesis) => (
            <li key={hypothesis.id} className="flex items-center gap-3 px-3 py-2">
              {hypothesis.signal ? (
                <Link
                  to="/hypotheses/$id"
                  params={{ id: hypothesis.id }}
                  className="font-mono hover:text-amber-300"
                >
                  {hypothesis.id} {hypothesis.name}
                </Link>
              ) : (
                <span className="font-mono text-zinc-500">
                  {hypothesis.id} {hypothesis.name}
                </span>
              )}
              {hypothesis.timeframe && (
                <span className="text-zinc-500">{hypothesis.timeframe}</span>
              )}
              {hypothesis.error && <span className="text-xs text-red-300">{hypothesis.error}</span>}
            </li>
          ))}
        </ul>
      </section>
    </main>
  );
}

type Mode = { readonly kind: 'rename' | 'copy'; readonly name: string } | null;

function SignalRow({ signal }: { readonly signal: SignalListItem }) {
  const queries = useQueryClient();
  const [mode, setMode] = useState<Mode>(null);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);

  const refresh = () => queries.invalidateQueries({ queryKey: ['signals'] });

  const submit = async () => {
    if (mode === null) {
      return;
    }

    setErrors([]);
    try {
      if (mode.kind === 'rename') {
        const outcome = await written(
          await api.POST('/api/signals/{name}/rename', {
            params: { path: { name: signal.name } },
            body: { to: mode.name },
          }),
        );
        if (!outcome.ok) {
          setErrors(outcome.errors);
          return;
        }

        moveDraft(draftKey({ name: signal.name }), draftKey({ name: mode.name }));
        queries.removeQueries({ queryKey: ['signal', signal.name] });
      } else {
        const source = unwrap(
          await api.GET('/api/signals/{name}', { params: { path: { name: signal.name } } }),
        );
        const outcome = await written(
          await api.POST('/api/signals', {
            body: {
              name: mode.name,
              description: source.description,
              graph: source.graph,
              layout: source.layout,
            },
          }),
        );
        if (!outcome.ok) {
          setErrors(outcome.errors);
          return;
        }
      }

      setMode(null);
      await refresh();
    } catch (problem) {
      setErrors([
        { code: 'Failed', reason: problem instanceof Error ? problem.message : String(problem) },
      ]);
    }
  };

  const remove = async () => {
    if (!window.confirm(`Удалить сигнал ${signal.name}? Незакоммиченный файл не вернуть.`)) {
      return;
    }

    await api.DELETE('/api/signals/{name}', { params: { path: { name: signal.name } } });
    removeDraft(draftKey({ name: signal.name }));
    queries.removeQueries({ queryKey: ['signal', signal.name] });
    await refresh();
  };

  return (
    <li aria-label={`Сигнал ${signal.name}`} className="flex flex-col gap-2 px-3 py-2">
      <div className="flex items-center gap-3">
        <Link
          to="/signals/$name"
          params={{ name: signal.name }}
          className="font-mono hover:text-amber-300"
        >
          {signal.name}
        </Link>
        {signal.description && <span className="text-zinc-400">{signal.description}</span>}
        <span className="ml-auto flex gap-2">
          <button
            type="button"
            className={buttonClass}
            onClick={() => setMode({ kind: 'rename', name: signal.name })}
          >
            Переименовать
          </button>
          {!signal.error && (
            <button
              type="button"
              className={buttonClass}
              onClick={() => setMode({ kind: 'copy', name: `${signal.name}-copy` })}
            >
              Копировать
            </button>
          )}
          <button type="button" className={buttonClass} onClick={remove}>
            Удалить
          </button>
        </span>
      </div>
      {signal.error && <p className="text-xs text-red-300">Файл не читается: {signal.error}</p>}
      {mode && (
        <form
          className="flex items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <input
            aria-label={mode.kind === 'rename' ? 'Новое имя' : 'Имя копии'}
            className={`${fieldClass} w-64`}
            value={mode.name}
            autoFocus
            onChange={(event) => setMode({ ...mode, name: event.target.value.trim() })}
          />
          <button type="submit" className={buttonClass} disabled={mode.name === ''}>
            {mode.kind === 'rename' ? 'Переименовать' : 'Скопировать'}
          </button>
          <button type="button" className={buttonClass} onClick={() => setMode(null)}>
            Отмена
          </button>
          {errors.length > 0 && (
            <span className="text-xs text-red-300">
              {errors.map((error) => error.reason).join('; ')}
            </span>
          )}
        </form>
      )}
    </li>
  );
}
