import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { api, unwrap } from '@/api/client';
import { written } from '@/api/signals';
import { listStrategies } from '@/api/strategies';
import { strategyDraftKey } from '@/modules/graph-editor/drafts/drafts';
import { buttonClass } from '@/modules/graph-editor/ui/Card';
import { DocumentRow, type ListType } from '@/pages/documents/DocumentRow';

import { SectionNav } from '../runs/SectionNav';

/** The row of a saved strategy: its words and the requests of `/api/strategies`. */
const strategyList: ListType = {
  label: 'Стратегия',
  queryKey: 'strategy',
  listKey: 'strategies',
  draftKey: strategyDraftKey,
  link: (name) => (
    <Link to="/strategies/$name" params={{ name }} className="font-mono hover:text-amber-300">
      {name}
    </Link>
  ),
  rename: async (name, to) =>
    written(
      await api.POST('/api/strategies/{name}/rename', { params: { path: { name } }, body: { to } }),
    ),
  copy: async (name, to) => {
    const source = unwrap(await api.GET('/api/strategies/{name}', { params: { path: { name } } }));
    return written(
      await api.POST('/api/strategies', {
        body: {
          name: to,
          description: source.description,
          graph: source.graph,
          layout: source.layout,
        },
      }),
    );
  },
  remove: async (name) => {
    await api.DELETE('/api/strategies/{name}', { params: { path: { name } } });
    return [];
  },
  deleteQuestion: (name) => `Удалить стратегию ${name} со всеми версиями?`,
};

/**
 * The section «Стратегии» (spec strategy-constructor «Список стратегий», «Создание, переименование, копирование и
 * удаление стратегии»): the saved strategies in the order of their name with their latest versions, one that does not
 * read with its error.
 */
export function StrategyListPage() {
  const navigate = useNavigate();
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });

  return (
    <main className="mx-auto flex max-w-4xl flex-col gap-6 p-6 text-sm">
      <header className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Стратегии</h1>
        <SectionNav />
        <button
          type="button"
          className={`${buttonClass} border-amber-500 px-3 py-1.5 text-sm`}
          onClick={() =>
            navigate({ to: '/strategies/new/$draftId', params: { draftId: crypto.randomUUID() } })
          }
        >
          Новая стратегия
        </button>
      </header>

      <section aria-label="Сохранённые стратегии" className="flex flex-col gap-2">
        {strategies.isPending && <p className="text-zinc-400">Загрузка…</p>}
        {strategies.error && <p className="text-red-300">{strategies.error.message}</p>}
        {strategies.data?.length === 0 && (
          <p className="text-zinc-400">
            Стратегий нет. Создайте новую — или из сигнала, кнопкой «Создать стратегию» в разделе
            «Сигналы».
          </p>
        )}
        <ul className="flex flex-col divide-y divide-zinc-800 rounded border border-zinc-800">
          {strategies.data?.map((strategy) => (
            <DocumentRow key={strategy.name} type={strategyList} line={strategy} />
          ))}
        </ul>
      </section>
    </main>
  );
}
