import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { api, unwrap } from '@/api/client';
import { listSignals, type SignalListItem, written } from '@/api/signals';
import { draftKey } from '@/modules/graph-editor/drafts/drafts';
import { DocumentTable, type ListType } from '@/pages/documents/DocumentTable';
import { openStrategyOfSignal } from '@/pages/strategies/fromSignal';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Page } from '@/shared/ui/Page';

/**
 * The section «Сигналы» (spec signal-constructor «Список сигналов», «Создание, переименование, копирование и
 * удаление», «Стратегии сигнала в списке»): the saved signals in the order of their name with the strategies that point
 * at each, and a file that does not read with its error.
 */
export function SignalListPage() {
  const navigate = useNavigate();
  const signals = useQuery({ queryKey: ['signals'], queryFn: listSignals });

  return (
    <Page>
      <DocumentTable
        type={signalList}
        title="Сигналы"
        hint="Условие входа как часть графа. Стратегия ссылается на версию сигнала."
        label="Сохранённые сигналы"
        lines={signals.data}
        pending={signals.isPending}
        problem={signals.error?.message ?? null}
        action={
          <Button
            variant="primary"
            onClick={() =>
              navigate({ to: '/signals/new/$draftId', params: { draftId: crypto.randomUUID() } })
            }
          >
            <span aria-hidden="true">+</span> Новый сигнал
          </Button>
        }
        empty={<p className="text-muted">Сигналов нет.</p>}
      />
    </Page>
  );
}

/** The table of the saved signals: its words, its column and the requests of `/api/signals`. */
const signalList: ListType<SignalListItem> = {
  label: 'Сигнал',
  object: 'сигнал',
  queryKey: 'signal',
  listKey: 'signals',
  draftKey,
  link: (name, className) => (
    <Link to="/signals/$name" params={{ name }} className={className}>
      {name}
    </Link>
  ),
  // The strategies any version of which points at the signal (spec signal-constructor «Стратегии сигнала в списке»).
  columns: [
    {
      title: 'Где используется',
      cell: (signal) =>
        (signal.strategies ?? []).length === 0 ? (
          <span className="text-faint">—</span>
        ) : (
          <span className="flex flex-wrap gap-1.5">
            {signal.strategies.map((strategy) => (
              <Link key={strategy} to="/strategies/$name" params={{ name: strategy }}>
                <Badge tone="accent" mono className="hover:brightness-125">
                  {strategy}
                </Badge>
              </Link>
            ))}
          </span>
        ),
    },
  ],
  actions: (signal) => <CreateStrategy signal={signal} />,
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
  deleteQuestion: (name) => `Удалить сигнал ${name} со всеми версиями?`,
};

/** «Создать стратегию» of a saved signal that reads: a new strategy with the signal as its node. */
function CreateStrategy({ signal }: { readonly signal: SignalListItem }) {
  const navigate = useNavigate();
  const id = signal.id;
  if (!id) {
    return null;
  }

  return (
    <Button
      variant="plain"
      className="text-muted! hover:text-text!"
      onClick={() =>
        void openStrategyOfSignal(navigate, {
          id,
          name: signal.name,
          version: Number(signal.version),
        })
      }
    >
      Создать стратегию
    </Button>
  );
}
