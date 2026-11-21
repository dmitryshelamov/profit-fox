import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { api, unwrap } from '@/api/client';
import { listSignals, type SignalListItem, written } from '@/api/signals';
import { draftKey } from '@/modules/graph-editor/drafts/drafts';
import { buttonClass } from '@/modules/graph-editor/ui/Card';
import { DocumentRow, type ListType } from '@/pages/documents/DocumentRow';
import { openStrategyOfSignal } from '@/pages/strategies/fromSignal';

import { RunControls } from './RunControls';

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
        {signals.data?.length === 0 && <p className="text-zinc-400">Сигналов нет.</p>}
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
            <li
              key={hypothesis.id}
              aria-label={`Гипотеза ${hypothesis.id}`}
              className="flex items-start gap-3 px-3 py-2"
            >
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
              <span className="text-zinc-500">версия {String(hypothesis.version)}</span>
              {hypothesis.error && <span className="text-xs text-red-300">{hypothesis.error}</span>}
              <RunControls hypothesis={hypothesis} />
            </li>
          ))}
        </ul>
      </section>
    </main>
  );
}

/** The row of a saved signal: its words and the requests of `/api/signals`. */
const signalList: ListType = {
  label: 'Сигнал',
  queryKey: 'signal',
  listKey: 'signals',
  draftKey,
  link: (name) => (
    <Link to="/signals/$name" params={{ name }} className="font-mono hover:text-amber-300">
      {name}
    </Link>
  ),
  rename: async (name, to) =>
    written(
      await api.POST('/api/signals/{name}/rename', { params: { path: { name } }, body: { to } }),
    ),
  copy: async (name, to) => {
    const source = unwrap(await api.GET('/api/signals/{name}', { params: { path: { name } } }));
    return written(
      await api.POST('/api/signals', {
        body: {
          name: to,
          description: source.description,
          graph: source.graph,
          layout: source.layout,
        },
      }),
    );
  },
  // A signal a strategy points at is not deleted (spec signal-constructor «Сигнал, на который ссылается стратегия»).
  remove: async (name) => {
    const answer = await api.DELETE('/api/signals/{name}', { params: { path: { name } } });
    if (answer.response.status === 409 && answer.error && 'strategies' in answer.error) {
      return [
        {
          code: 'SignalInUse',
          reason: `Сигнал используют стратегии: ${(answer.error.strategies as string[]).join(', ')}`,
        },
      ];
    }

    return [];
  },
  deleteQuestion: (name) => `Удалить сигнал ${name}? Незакоммиченный файл не вернуть.`,
};

function SignalRow({ signal }: { readonly signal: SignalListItem }) {
  const navigate = useNavigate();
  return (
    <DocumentRow
      type={signalList}
      line={signal}
      actions={
        !signal.error &&
        signal.id && (
          <button
            type="button"
            className={buttonClass}
            onClick={() =>
              void openStrategyOfSignal(navigate, {
                id: signal.id!,
                name: signal.name,
                version: Number(signal.version),
              })
            }
          >
            Создать стратегию
          </button>
        )
      }
    />
  );
}
