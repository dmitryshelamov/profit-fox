import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { catalogQuery, cloneNode, deleteNode } from '@/modules/nodes';
import { ApiError, type Schemas } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { PinChip } from '@/shared/ui/PinChip';

type CatalogNode = Schemas['NodeResponse'];

export function NodesPage() {
  const catalog = useQuery(catalogQuery);
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState<CatalogNode>();
  const [failure, setFailure] = useState<string>();

  async function run(action: () => Promise<unknown>) {
    setDeleting(undefined);
    setFailure(undefined);

    try {
      await action();
    } catch (error) {
      setFailure(describe(error));
    }

    await queryClient.invalidateQueries({ queryKey: catalogQuery.queryKey });
  }

  const section = (title: string, nodes: readonly CatalogNode[], builtIn: boolean) => (
    <section aria-label={title} className="space-y-3">
      <h2 className="text-[15px] font-semibold">{title}</h2>
      <ul className="space-y-2">
        {nodes.map((node) => (
          <li
            key={node.id}
            className="grid grid-cols-[260px_1fr_220px_auto] items-center gap-4 rounded-lg border border-line bg-panel px-5 py-3"
          >
            <span>
              <Link
                to="/nodes/$id"
                params={{ id: node.id }}
                className="font-semibold hover:underline"
              >
                {node.title}
              </Link>
              <span className={`block text-xs ${builtIn ? 'text-[#c9a45e]' : 'text-faint'}`}>
                {builtIn ? 'встроенный · только чтение' : 'мой'}
              </span>
            </span>
            <span className="flex flex-wrap gap-1.5">
              {node.inputs.map((pin) => (
                <PinChip key={pin.name} {...pin} />
              ))}
            </span>
            <span className="flex flex-wrap gap-1.5">
              {node.outputs.length > 0 ? (
                node.outputs.map((pin) => <PinChip key={pin.name} {...pin} />)
              ) : (
                <span className="text-xs text-faint">— (ордера внутри)</span>
              )}
            </span>
            <span className="flex gap-1">
              <Button
                look="plain"
                aria-label={`Клонировать: ${node.title}`}
                onClick={() => void run(() => cloneNode(node.id))}
              >
                Клонировать
              </Button>
              {!builtIn && (
                <Button
                  look="danger"
                  aria-label={`Удалить: ${node.title}`}
                  onClick={() => setDeleting(node)}
                >
                  Удалить
                </Button>
              )}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );

  return (
    <section className="space-y-8">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Составные узлы</h1>
        <p className="text-sm text-muted">
          Узлы, собранные из других узлов. Свои правятся, встроенные — только клонируются.
        </p>
      </header>
      {failure && (
        <p role="alert" className="rounded-lg border border-[#5a2a2e] bg-err-bg p-3 text-sm">
          {failure}
        </p>
      )}
      {catalog.data && (
        <>
          {section(
            'Мои',
            catalog.data.filter((node) => node.group === 'custom'),
            false,
          )}
          {section(
            'Встроенные',
            catalog.data.filter((node) => node.group === 'builtIn'),
            true,
          )}
        </>
      )}
      {deleting && (
        <Dialog
          title={`Удалить узел «${deleting.title}»?`}
          actions={
            <>
              <Button onClick={() => setDeleting(undefined)}>Отмена</Button>
              <Button look="danger" onClick={() => void run(() => deleteNode(deleting.id))}>
                Удалить
              </Button>
            </>
          }
        />
      )}
    </section>
  );
}

function describe(error: unknown): string {
  if (error instanceof ApiError && error.usages.length > 0) {
    return `Удалить нельзя: узел используется в ${error.usages.map((usage) => `«${usage.name}»`).join(', ')}.`;
  }

  return (error as Error).message;
}
