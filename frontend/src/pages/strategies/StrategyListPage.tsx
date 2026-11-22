import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { api, unwrap } from '@/api/client';
import { written } from '@/api/signals';
import { listStrategies, type StrategyListItem } from '@/api/strategies';
import { strategyDraftKey } from '@/modules/graph-editor/drafts/drafts';
import { DocumentTable, type ListType } from '@/pages/documents/DocumentTable';
import { Badge } from '@/shared/ui/Badge';
import { Button, buttonLook } from '@/shared/ui/Button';
import { Page } from '@/shared/ui/Page';

/** The kinds of the exit nodes in the words of the list, in the order the API gives them. */
const exitWords: Record<string, string> = {
  'stop-loss': 'стоп',
  'take-profit': 'цель',
  'close-after': 'срок',
  'close-position': 'закрытие',
  'limit-order': 'лимитка',
};

/** The table of the saved strategies: its words, its columns and the requests of `/api/strategies`. */
const strategyList: ListType<StrategyListItem> = {
  label: 'Стратегия',
  object: 'стратегию',
  queryKey: 'strategy',
  listKey: 'strategies',
  draftKey: strategyDraftKey,
  link: (name, className) => (
    <Link to="/strategies/$name" params={{ name }} className={className}>
      {name}
    </Link>
  ),
  // The signals of the latest version and its exits (spec strategy-constructor «Сигналы и выходы стратегии в списке»);
  // a signal with a newer version says so, and only the editor moves the reference («Отметка новой версии сигнала»).
  columns: [
    {
      title: 'Сигналы',
      cell: (strategy) =>
        (strategy.signals ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="flex flex-wrap items-center gap-1.5">
            {strategy.signals.map((signal) => (
              <span
                key={`${signal.name}:${signal.version}`}
                className="inline-flex items-center gap-1.5"
              >
                <Link to="/signals/$name" params={{ name: signal.name }}>
                  <Badge tone="accent" mono className="hover:brightness-125">
                    {signal.name} v{String(signal.version)}
                  </Badge>
                </Link>
                {Number(signal.latestVersion) > Number(signal.version) && (
                  <Badge tone="warn">есть v{String(signal.latestVersion)}</Badge>
                )}
              </span>
            ))}
          </span>
        ),
    },
    {
      title: 'Выход',
      className: 'w-[180px]',
      cell: (strategy) =>
        (strategy.exits ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="text-xs text-muted">
            {strategy.exits.map((exit) => exitWords[exit] ?? exit).join(' · ')}
          </span>
        ),
    },
  ],
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
 * удаление стратегии», «Сигналы и выходы стратегии в списке»): the saved strategies in the order of their name with
 * their latest versions, signals and exits, one that does not read with its error.
 */
export function StrategyListPage() {
  const navigate = useNavigate();
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  const create = (
    <Button
      variant="primary"
      onClick={() =>
        navigate({ to: '/strategies/new/$draftId', params: { draftId: crypto.randomUUID() } })
      }
    >
      <span aria-hidden="true">+</span> Новая стратегия
    </Button>
  );
  // The empty list offers the new strategy itself, so the screen keeps one main button; until the list has come the
  // title has no button, so it does not move from the title into the empty list under the pointer.
  const titled = strategies.error !== null || (strategies.data?.length ?? 0) > 0;

  return (
    <Page>
      <DocumentTable
        type={strategyList}
        title="Стратегии"
        hint="Полный граф: сигналы, вход и правила выхода. Каждое сохранение — новая версия."
        label="Сохранённые стратегии"
        lines={strategies.data}
        pending={strategies.isPending}
        problem={strategies.error?.message ?? null}
        action={titled ? create : null}
        empty={
          <div className="flex flex-col items-center gap-3 rounded-[10px] border border-dashed border-line bg-panel px-6 py-16">
            <h2 className="text-base font-bold">Стратегий нет</h2>
            <p className="text-muted">Соберите граф с нуля или начните с готового сигнала.</p>
            <div className="mt-2 flex items-center gap-2">
              {create}
              <Link to="/signals" className={buttonLook('ghost')}>
                Выбрать сигнал
              </Link>
            </div>
          </div>
        }
      />
    </Page>
  );
}
